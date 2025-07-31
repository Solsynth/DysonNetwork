using CorePush.Apple;
using CorePush.Firebase;
using DysonNetwork.Pusher.Connection;
using DysonNetwork.Shared.Proto;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pusher.Notification;

public class PushService
{
    private readonly AppDatabase _db;
    private readonly WebSocketService _ws;
    private readonly ILogger<PushService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;
    private readonly string? _apnsTopic;

    public PushService(
        IConfiguration config,
        AppDatabase db,
        WebSocketService ws,
        IHttpClientFactory httpFactory,
        ILogger<PushService> logger
    )
    {
        var cfgSection = config.GetSection("Notifications:Push");

        // Set up Firebase Cloud Messaging
        var fcmConfig = cfgSection.GetValue<string>("Google");
        if (fcmConfig != null && File.Exists(fcmConfig))
            _fcm = new FirebaseSender(File.ReadAllText(fcmConfig), httpFactory.CreateClient());

        // Set up Apple Push Notification Service
        var apnsKeyPath = cfgSection.GetValue<string>("Apple:PrivateKey");
        if (apnsKeyPath != null && File.Exists(apnsKeyPath))
        {
            _apns = new ApnSender(new ApnSettings
            {
                P8PrivateKey = File.ReadAllText(apnsKeyPath),
                P8PrivateKeyId = cfgSection.GetValue<string>("Apple:PrivateKeyId"),
                TeamId = cfgSection.GetValue<string>("Apple:TeamId"),
                AppBundleIdentifier = cfgSection.GetValue<string>("Apple:BundleIdentifier"),
                ServerType = cfgSection.GetValue<bool>("Production")
                    ? ApnServerType.Production
                    : ApnServerType.Development
            }, httpFactory.CreateClient());
            _apnsTopic = cfgSection.GetValue<string>("Apple:BundleIdentifier");
        }

        _db = db;
        _ws = ws;
        _logger = logger;
    }

    public async Task UnsubscribeDevice(string deviceId)
    {
        await _db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync();
    }

    public async Task<PushSubscription> SubscribeDevice(
        string deviceId,
        string deviceToken,
        PushProvider provider,
        Account account
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(account.Id!);

        // Check for existing subscription with same device ID or token
        var existingSubscription = await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceToken == deviceToken)
            .FirstOrDefaultAsync();

        if (existingSubscription != null)
        {
            // Update existing subscription
            existingSubscription.DeviceId = deviceId;
            existingSubscription.DeviceToken = deviceToken;
            existingSubscription.Provider = provider;
            existingSubscription.UpdatedAt = now;

            _db.Update(existingSubscription);
            await _db.SaveChangesAsync();
            return existingSubscription;
        }

        // Create new subscription
        var subscription = new PushSubscription
        {
            DeviceId = deviceId,
            DeviceToken = deviceToken,
            Provider = provider,
            AccountId = accountId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.PushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        return subscription;
    }

    public async Task SendNotification(Account account,
        string topic,
        string? title = null,
        string? subtitle = null,
        string? content = null,
        Dictionary<string, object?> meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool save = true)
    {
        if (title is null && subtitle is null && content is null)
            throw new ArgumentException("Unable to send notification that completely empty.");

        if (actionUri is not null) meta["action_uri"] = actionUri;

        var accountId = Guid.Parse(account.Id!);
        var notification = new Notification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = accountId,
        };

        if (save)
        {
            _db.Add(notification);
            await _db.SaveChangesAsync();
        }

        if (!isSilent) _ = DeliveryNotification(notification);
    }

    private async Task DeliveryNotification(Notification notification)
    {
        _logger.LogInformation(
            "Delivering notification: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        // Pushing the notification
        var subscribers = await _db.PushSubscriptions
            .Where(s => s.AccountId == notification.AccountId)
            .ToListAsync();

        await _PushNotification(notification, subscribers);
    }

    public async Task MarkNotificationsViewed(ICollection<Notification> notifications)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var id = notifications.Where(n => n.ViewedAt == null).Select(n => n.Id).ToList();
        if (id.Count == 0) return;

        await _db.Notifications
            .Where(n => id.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now)
            );
    }

    public async Task SendNotificationBatch(Notification notification, List<Guid> accounts, bool save = false)
    {
        if (save)
        {
            var notifications = accounts.Select(x =>
            {
                var newNotification = new Notification
                {
                    Topic = notification.Topic,
                    Title = notification.Title,
                    Subtitle = notification.Subtitle,
                    Content = notification.Content,
                    Meta = notification.Meta,
                    Priority = notification.Priority,
                    AccountId = x
                };
                return newNotification;
            }).ToList();
            await _db.BulkInsertAsync(notifications);
        }
        
        _logger.LogInformation(
            "Delivering notification in batch: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        var subscribers = await _db.PushSubscriptions
            .Where(s => accounts.Contains(s.AccountId))
            .ToListAsync();
        await _PushNotification(notification, subscribers);
    }

    private async Task _PushNotification(
        Notification notification,
        IEnumerable<PushSubscription> subscriptions
    )
    {
        var tasks = subscriptions
            .Select(subscription => _PushSingleNotification(notification, subscription))
            .ToList();

        await Task.WhenAll(tasks);
    }

    private async Task _PushSingleNotification(Notification notification, PushSubscription subscription)
    {
        try
        {
            _logger.LogDebug(
                $"Pushing notification {notification.Topic} #{notification.Id} to device #{subscription.DeviceId}");

            switch (subscription.Provider)
            {
                case PushProvider.Google:
                    if (_fcm == null)
                        throw new InvalidOperationException("Firebase Cloud Messaging is not initialized.");

                    var body = string.Empty;
                    if (!string.IsNullOrEmpty(notification.Subtitle) || !string.IsNullOrEmpty(notification.Content))
                    {
                        body = string.Join("\n",
                            notification.Subtitle ?? string.Empty,
                            notification.Content ?? string.Empty).Trim();
                    }

                    await _fcm.SendAsync(new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object>
                        {
                            ["token"] = subscription.DeviceToken,
                            ["notification"] = new Dictionary<string, object>
                            {
                                ["title"] = notification.Title ?? string.Empty,
                                ["body"] = body
                            },
                            ["data"] = new Dictionary<string, object>
                            {
                                ["id"] = notification.Id,
                                ["topic"] = notification.Topic,
                                ["meta"] = notification.Meta
                            }
                        }
                    });
                    break;

                case PushProvider.Apple:
                    if (_apns == null)
                        throw new InvalidOperationException("Apple Push Notification Service is not initialized.");

                    var alertDict = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(notification.Title))
                        alertDict["title"] = notification.Title;
                    if (!string.IsNullOrEmpty(notification.Subtitle))
                        alertDict["subtitle"] = notification.Subtitle;
                    if (!string.IsNullOrEmpty(notification.Content))
                        alertDict["body"] = notification.Content;

                    var payload = new Dictionary<string, object?>
                    {
                        ["topic"] = _apnsTopic,
                        ["type"] = notification.Topic,
                        ["aps"] = new Dictionary<string, object?>
                        {
                            ["alert"] = alertDict,
                            ["sound"] = notification.Priority >= 5 ? "default" : null,
                            ["mutable-content"] = 1
                        },
                        ["meta"] = notification.Meta
                    };

                    await _apns.SendAsync(
                        payload,
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: ApnPushType.Alert
                    );
                    break;

                default:
                    throw new InvalidOperationException($"Push provider not supported: {subscription.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Failed to push notification #{notification.Id} to device {subscription.DeviceId}. {ex.Message}");
            throw new Exception($"Failed to send notification to {subscription.Provider}: {ex.Message}", ex);
        }

        _logger.LogInformation(
            $"Successfully pushed notification #{notification.Id} to device {subscription.DeviceId}");
    }
}