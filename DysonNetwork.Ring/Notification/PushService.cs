using CorePush.Apple;
using CorePush.Firebase;
using DysonNetwork.Ring.Connection;
using DysonNetwork.Ring.Services;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Ring.Notification;

public class PushService
{
    private readonly AppDatabase _db;
    private readonly QueueService _queueService;
    private readonly ILogger<PushService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;
    private readonly FlushBufferService _fbs;
    private readonly string? _apnsTopic;

    public PushService(
        IConfiguration config,
        AppDatabase db,
        QueueService queueService,
        FlushBufferService fbs,
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
        _fbs = fbs;
        _queueService = queueService;
        _logger = logger;
    }

    public async Task UnsubscribeDevice(string deviceId)
    {
        await _db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync();
    }

    public async Task<SnNotificationPushSubscription> SubscribeDevice(
        string deviceId,
        string deviceToken,
        PushProvider provider,
        Account account
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(account.Id);

        // Check for existing subscription with the same device ID or token
        var existingSubscription = await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceToken == deviceToken)
            .FirstOrDefaultAsync();

        if (existingSubscription != null)
        {
            existingSubscription.DeviceId = deviceId;
            existingSubscription.DeviceToken = deviceToken;
            existingSubscription.Provider = provider;
            existingSubscription.UpdatedAt = now;

            _db.Update(existingSubscription);
            await _db.SaveChangesAsync();
            return existingSubscription;
        }

        var subscription = new SnNotificationPushSubscription
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
        Dictionary<string, object?>? meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool save = true)
    {
        meta ??= [];
        if (title is null && subtitle is null && content is null)
            throw new ArgumentException("Unable to send notification that is completely empty.");

        if (actionUri is not null) meta["action_uri"] = actionUri;

        var accountId = account.Id;
        var notification = new SnNotification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = Guid.Parse(accountId),
        };

        if (save)
        {
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }

        if (!isSilent)
            _ = _queueService.EnqueuePushNotification(notification, Guid.Parse(accountId), save);
    }

    public async Task DeliverPushNotification(SnNotification notification,
        CancellationToken cancellationToken = default)
    {
        WebSocketService.SendPacketToAccount(notification.AccountId, new WebSocketPacket()
        {
            Type = "notifications.new",
            Data = notification,
        });

        try
        {
            _logger.LogInformation(
                "Delivering push notification: {NotificationTopic} with meta {NotificationMeta}",
                notification.Topic,
                notification.Meta
            );

            // Get all push subscriptions for the account
            var subscriptions = await _db.PushSubscriptions
                .Where(s => s.AccountId == notification.AccountId)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                _logger.LogInformation("No push subscriptions found for account {AccountId}", notification.AccountId);
                return;
            }

            // Send push notifications
            var tasks = new List<Task>();
            foreach (var subscription in subscriptions)
            {
                try
                {
                    tasks.Add(SendPushNotificationAsync(subscription, notification));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending push notification to {DeviceId}", subscription.DeviceId);
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeliverPushNotification");
            throw;
        }
    }

    public async Task MarkNotificationsViewed(ICollection<SnNotification> notifications)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var id = notifications.Where(n => n.ViewedAt == null).Select(n => n.Id).ToList();
        if (id.Count == 0) return;

        await _db.Notifications
            .Where(n => id.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task MarkAllNotificationsViewed(Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await _db.Notifications
            .Where(n => n.AccountId == accountId)
            .Where(n => n.ViewedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task SendNotificationBatch(SnNotification notification, List<Guid> accounts, bool save = false)
    {
        if (save)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var notifications = accounts.Select(accountId => new SnNotification
            {
                Topic = notification.Topic,
                Title = notification.Title,
                Subtitle = notification.Subtitle,
                Content = notification.Content,
                Meta = notification.Meta,
                Priority = notification.Priority,
                AccountId = accountId,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            if (notifications.Count != 0)
            {
                await _db.Notifications.AddRangeAsync(notifications);
                await _db.SaveChangesAsync();
            }
        }

        _logger.LogInformation(
            "Delivering notification in batch: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        // WS first
        foreach (var account in accounts)
        {
            notification.AccountId = account;
            WebSocketService.SendPacketToAccount(account, new WebSocketPacket
            {
                Type = "notifications.new",
                Data = notification
            });
        }

        await DeliverPushNotification(notification);
    }

    private async Task SendPushNotificationAsync(SnNotificationPushSubscription subscription,
        SnNotification notification)
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
                            notification.Content ?? string.Empty
                        ).Trim();
                    }

                    var fcmResult = await _fcm.SendAsync(new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object>
                        {
                            ["token"] = subscription.DeviceToken,
                            ["notification"] = new Dictionary<string, object>
                            {
                                ["title"] = notification.Title ?? string.Empty,
                                ["body"] = body
                            },
                            // You can re-enable data payloads if needed.
                            // ["data"] = new Dictionary<string, object>
                            // {
                            //     ["Id"] = notification.Id,
                            //     ["Topic"] = notification.Topic,
                            //     ["Meta"] = notification.Meta
                            // }
                        }
                    });

                    if (fcmResult.StatusCode is 404 or 410)
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (fcmResult.Error != null)
                        throw new Exception($"Notification pushed failed ({fcmResult.StatusCode}) {fcmResult.Error}");
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

                    var apnResult = await _apns.SendAsync(
                        payload,
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: ApnPushType.Alert
                    );
                    
                    if (apnResult.StatusCode is 404 or 410)
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (apnResult.Error != null)
                        throw new Exception($"Notification pushed failed ({apnResult.StatusCode}) {apnResult.Error}");

                    break;

                default:
                    throw new InvalidOperationException($"Push provider not supported: {subscription.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Failed to push notification #{notification.Id} to device {subscription.DeviceId}. {ex.Message}");
            // Swallow here to keep worker alive; upstream is fire-and-forget.
        }

        _logger.LogInformation(
            $"Successfully pushed notification #{notification.Id} to device {subscription.DeviceId} provider {subscription.Provider}");
    }

    public async Task SaveNotification(SnNotification notification)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();
    }

    public async Task SaveNotification(SnNotification notification, List<Guid> accounts)
    {
        _db.Notifications.AddRange(accounts.Select(a => new SnNotification
        {
            AccountId = a,
            Topic = notification.Topic,
            Content = notification.Content,
            Title = notification.Title,
            Subtitle = notification.Subtitle,
            Meta = notification.Meta,
            Priority = notification.Priority,
            CreatedAt = notification.CreatedAt,
            UpdatedAt = notification.UpdatedAt,
        }));
        await _db.SaveChangesAsync();
    }
}