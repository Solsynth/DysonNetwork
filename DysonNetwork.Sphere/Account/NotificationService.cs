using CorePush.Apple;
using CorePush.Firebase;
using DysonNetwork.Sphere.Connection;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class NotificationService
{
    private readonly AppDatabase _db;
    private readonly WebSocketService _ws;
    private readonly ILogger<NotificationService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;

    public NotificationService(
        AppDatabase db,
        WebSocketService ws,
        IConfiguration cfg,
        IHttpClientFactory clientFactory,
        ILogger<NotificationService> logger
    )
    {
        _db = db;
        _ws = ws;
        _logger = logger;

        var cfgSection = cfg.GetSection("Notifications:Push");

        // Set up the firebase push notification
        var fcmConfig = cfgSection.GetValue<string>("Google");
        if (fcmConfig != null)
            _fcm = new FirebaseSender(File.ReadAllText(fcmConfig), clientFactory.CreateClient());
        // Set up the apple push notification service
        var apnsCert = cfgSection.GetValue<string>("Apple:PrivateKey");
        if (apnsCert != null)
            _apns = new ApnSender(new ApnSettings
            {
                P8PrivateKey = File.ReadAllText(apnsCert),
                P8PrivateKeyId = cfgSection.GetValue<string>("Apple:PrivateKeyId"),
                TeamId = cfgSection.GetValue<string>("Apple:TeamId"),
                AppBundleIdentifier = cfgSection.GetValue<string>("Apple:BundleIdentifier"),
                ServerType = cfgSection.GetValue<bool>("Production")
                    ? ApnServerType.Production
                    : ApnServerType.Development
            }, clientFactory.CreateClient());
    }

    // TODO remove all push notification with this device id when this device is logged out

    public async Task<NotificationPushSubscription> SubscribePushNotification(
        Account account,
        NotificationPushProvider provider,
        string deviceId,
        string deviceToken
    )
    {
        var existingSubscription = await _db.NotificationPushSubscriptions
            .Where(s => s.AccountId == account.Id)
            .Where(s => s.DeviceId == deviceId || s.DeviceToken == deviceToken)
            .FirstOrDefaultAsync();

        if (existingSubscription != null)
        {
            // Reset these audit fields to renew the lifecycle of this device token
            existingSubscription.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            existingSubscription.UpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            _db.Update(existingSubscription);
            await _db.SaveChangesAsync();
            return existingSubscription;
        }

        var subscription = new NotificationPushSubscription
        {
            DeviceId = deviceId,
            DeviceToken = deviceToken,
            Provider = provider,
            AccountId = account.Id,
        };

        _db.NotificationPushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        return subscription;
    }

    public async Task<Notification> SendNotification(
        Account account,
        string topic,
        string? title = null,
        string? subtitle = null,
        string? content = null,
        Dictionary<string, object>? meta = null,
        bool isSilent = false
    )
    {
        if (title is null && subtitle is null && content is null)
        {
            throw new ArgumentException("Unable to send notification that completely empty.");
        }

        var notification = new Notification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = account.Id,
        };

        _db.Add(notification);
        await _db.SaveChangesAsync();

        if (!isSilent) _ = DeliveryNotification(notification);

        return notification;
    }

    public async Task DeliveryNotification(Notification notification)
    {
        _ws.SendPacketToAccount(notification.AccountId, new WebSocketPacket
        {
            Type = "notifications.new",
            Data = notification
        });

        // Pushing the notification
        var subscribers = await _db.NotificationPushSubscriptions
            .Where(s => s.AccountId == notification.AccountId)
            .ToListAsync();

        var tasks = subscribers
            .Select(subscriber => _PushSingleNotification(notification, subscriber))
            .ToList();

        await Task.WhenAll(tasks);
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

    public async Task BroadcastNotification(Notification notification, bool save = false)
    {
        if (save)
        {
            var accounts = await _db.Accounts.ToListAsync();
            var notifications = accounts.Select(x =>
            {
                notification.Account = x;
                notification.AccountId = x.Id;
                return notification;
            }).ToList();
            await _db.BulkInsertAsync(notifications);
        }

        var subscribers = await _db.NotificationPushSubscriptions
            .ToListAsync();
        var tasks = new List<Task>();
        foreach (var subscriber in subscribers)
        {
            notification.AccountId = subscriber.AccountId;
            tasks.Add(_PushSingleNotification(notification, subscriber));
        }

        await Task.WhenAll(tasks);
    }

    public async Task SendNotificationBatch(Notification notification, List<Account> accounts, bool save = false)
    {
        if (save)
        {
            var notifications = accounts.Select(x =>
            {
                notification.Account = x;
                notification.AccountId = x.Id;
                return notification;
            }).ToList();
            await _db.BulkInsertAsync(notifications);
        }

        var accountsId = accounts.Select(x => x.Id).ToList();
        var subscribers = await _db.NotificationPushSubscriptions
            .Where(s => accountsId.Contains(s.AccountId))
            .ToListAsync();
        var tasks = new List<Task>();
        foreach (var subscriber in subscribers)
        {
            notification.AccountId = subscriber.AccountId;
            tasks.Add(_PushSingleNotification(notification, subscriber));
        }

        await Task.WhenAll(tasks);
    }

    private async Task _PushSingleNotification(Notification notification, NotificationPushSubscription subscription)
    {
        try
        {
            var body = string.Empty;
            switch (subscription.Provider)
            {
                case NotificationPushProvider.Google:
                    if (_fcm == null)
                        throw new InvalidOperationException("The firebase cloud messaging is not initialized.");

                    if (!string.IsNullOrEmpty(notification.Subtitle) || !string.IsNullOrEmpty(notification.Content))
                    {
                        body = string.Join("\n",
                            notification.Subtitle ?? string.Empty,
                            notification.Content ?? string.Empty).Trim();
                    }

                    await _fcm.SendAsync(new
                    {
                        message = new
                        {
                            token = subscription.DeviceToken,
                            notification = new
                            {
                                title = notification.Title ?? string.Empty, body
                            },
                            data = notification.Meta ?? new Dictionary<string, object>()
                        }
                    });
                    break;

                case NotificationPushProvider.Apple:
                    if (_apns == null)
                        throw new InvalidOperationException("The apple notification push service is not initialized.");

                    await _apns.SendAsync(new
                        {
                            aps = new
                            {
                                alert = new
                                {
                                    title = notification.Title ?? string.Empty,
                                    subtitle = notification.Subtitle ?? string.Empty,
                                    body = notification.Content ?? string.Empty
                                }
                            },
                            meta = notification.Meta ?? new Dictionary<string, object>()
                        },
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: ApnPushType.Alert
                    );
                    break;

                default:
                    throw new InvalidOperationException($"Provider not supported: {subscription.Provider}");
            }
        }
        catch (Exception ex)
        {
            // Log the exception
            // Consider implementing a retry mechanism
            // Rethrow or handle as needed
            throw new Exception($"Failed to send notification to {subscription.Provider}: {ex.Message}", ex);
        }
    }
}