using CorePush.Apple;
using CorePush.Firebase;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class NotificationService
{
    private readonly AppDatabase _db;
    private readonly ILogger<NotificationService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;

    public NotificationService(
        AppDatabase db,
        IConfiguration cfg,
        IHttpClientFactory clientFactory,
        ILogger<NotificationService> logger
    )
    {
        _db = db;
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
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            Account = account,
            AccountId = account.Id,
        };

        _db.Add(notification);
        await _db.SaveChangesAsync();

#pragma warning disable CS4014
        if (!isSilent) DeliveryNotification(notification);
#pragma warning restore CS4014

        return notification;
    }

    public async Task DeliveryNotification(Notification notification)
    {
        // TODO send websocket

        // Pushing the notification
        var subscribers = await _db.NotificationPushSubscriptions
            .Where(s => s.AccountId == notification.AccountId)
            .ToListAsync();

        var tasks = new List<Task>();
        foreach (var subscriber in subscribers)
        {
            tasks.Add(_PushSingleNotification(notification, subscriber));
        }

        await Task.WhenAll(tasks);
    }

    private async Task _PushSingleNotification(Notification notification, NotificationPushSubscription subscription)
    {
        switch (subscription.Provider)
        {
            case NotificationPushProvider.Google:
                if (_fcm == null)
                    throw new InvalidOperationException("The firebase cloud messaging is not initialized.");
                await _fcm.SendAsync(new
                {
                    message = new
                    {
                        token = subscription.DeviceToken,
                        notification = new
                        {
                            title = notification.Title,
                            body = string.Join("\n", notification.Subtitle, notification.Content),
                        },
                        data = notification.Meta
                    }
                });
                break;
            case NotificationPushProvider.Apple:
                if (_apns == null)
                    throw new InvalidOperationException("The apple notification push service is not initialized.");
                await _apns.SendAsync(new
                    {
                        apns = new
                        {
                            alert = new
                            {
                                title = notification.Title,
                                subtitle = notification.Subtitle,
                                content = notification.Content,
                            }
                        },
                        meta = notification.Meta,
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
}