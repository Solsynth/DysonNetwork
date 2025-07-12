using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Proto;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pusher.Notification;

public class PushService(IConfiguration config, AppDatabase db, IHttpClientFactory httpFactory)
{
    private readonly string _notifyTopic = config["Notifications:Topic"]!;
    private readonly Uri _notifyEndpoint = new(config["Notifications:Endpoint"]!);

    public async Task UnsubscribePushNotifications(string deviceId)
    {
        await db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync();
    }

    public async Task<PushSubscription> SubscribePushNotification(
        string deviceId,
        string deviceToken,
        PushProvider provider,
        Account account
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        // First check if a matching subscription exists
        var accountId = Guid.Parse(account.Id!);
        var existingSubscription = await db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceToken == deviceToken)
            .FirstOrDefaultAsync();

        if (existingSubscription is not null)
        {
            // Update the existing subscription directly in the database
            await db.PushSubscriptions
                .Where(s => s.Id == existingSubscription.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.DeviceId, deviceId)
                    .SetProperty(s => s.DeviceToken, deviceToken)
                    .SetProperty(s => s.UpdatedAt, now));

            // Return the updated subscription
            existingSubscription.DeviceId = deviceId;
            existingSubscription.DeviceToken = deviceToken;
            existingSubscription.UpdatedAt = now;
            return existingSubscription;
        }

        var subscription = new PushSubscription
        {
            DeviceId = deviceId,
            DeviceToken = deviceToken,
            Provider = provider,
            AccountId = accountId,
        };

        db.PushSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    public async Task<Pusher.Notification.Notification> SendNotification(
        Account account,
        string topic,
        string? title = null,
        string? subtitle = null,
        string? content = null,
        Dictionary<string, object>? meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool save = true
    )
    {
        if (title is null && subtitle is null && content is null)
            throw new ArgumentException("Unable to send notification that completely empty.");

        meta ??= new Dictionary<string, object>();
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
            db.Add(notification);
            await db.SaveChangesAsync();
        }

        if (!isSilent) _ = DeliveryNotification(notification);

        return notification;
    }

    public async Task DeliveryNotification(Pusher.Notification.Notification notification)
    {
        // Pushing the notification
        var subscribers = await db.PushSubscriptions
            .Where(s => s.AccountId == notification.AccountId)
            .ToListAsync();

        await _PushNotification(notification, subscribers);
    }

    public async Task MarkNotificationsViewed(ICollection<Notification> notifications)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var id = notifications.Where(n => n.ViewedAt == null).Select(n => n.Id).ToList();
        if (id.Count == 0) return;

        await db.Notifications
            .Where(n => id.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now)
            );
    }

    public async Task SendNotificationBatch(Notification notification, List<Account> accounts, bool save = false)
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
                    Account = x,
                    AccountId = Guid.Parse(x.Id)
                };
                return newNotification;
            }).ToList();
            await db.BulkInsertAsync(notifications);
        }

        foreach (var account in accounts)
        {
            notification.Account = account;
            notification.AccountId = Guid.Parse(account.Id);
        }

        var accountsId = accounts.Select(x => Guid.Parse(x.Id)).ToList();
        var subscribers = await db.PushSubscriptions
            .Where(s => accountsId.Contains(s.AccountId))
            .ToListAsync();
        await _PushNotification(notification, subscribers);
    }

    private List<Dictionary<string, object>> _BuildNotificationPayload(Notification notification,
        IEnumerable<PushSubscription> subscriptions)
    {
        var subDict = subscriptions
            .GroupBy(x => x.Provider)
            .ToDictionary(x => x.Key, x => x.ToList());

        var notifications = subDict.Select(value =>
        {
            var platformCode = value.Key switch
            {
                PushProvider.Apple => 1,
                PushProvider.Google => 2,
                _ => throw new InvalidOperationException($"Unknown push provider: {value.Key}")
            };

            var tokens = value.Value.Select(x => x.DeviceToken).ToList();
            return _BuildNotificationPayload(notification, platformCode, tokens);
        }).ToList();

        return notifications.ToList();
    }

    private Dictionary<string, object> _BuildNotificationPayload(Pusher.Notification.Notification notification,
        int platformCode,
        IEnumerable<string> deviceTokens)
    {
        var alertDict = new Dictionary<string, object>();
        var dict = new Dictionary<string, object>
        {
            ["notif_id"] = notification.Id.ToString(),
            ["apns_id"] = notification.Id.ToString(),
            ["topic"] = _notifyTopic,
            ["tokens"] = deviceTokens,
            ["data"] = new Dictionary<string, object>
            {
                ["type"] = notification.Topic,
                ["meta"] = notification.Meta ?? new Dictionary<string, object>(),
            },
            ["mutable_content"] = true,
            ["priority"] = notification.Priority >= 5 ? "high" : "normal",
        };

        if (!string.IsNullOrWhiteSpace(notification.Title))
        {
            dict["title"] = notification.Title;
            alertDict["title"] = notification.Title;
        }

        if (!string.IsNullOrWhiteSpace(notification.Content))
        {
            dict["message"] = notification.Content;
            alertDict["body"] = notification.Content;
        }

        if (!string.IsNullOrWhiteSpace(notification.Subtitle))
        {
            dict["message"] = $"{notification.Subtitle}\n{dict["message"]}";
            alertDict["subtitle"] = notification.Subtitle;
        }

        if (notification.Priority >= 5)
            dict["name"] = "default";

        dict["platform"] = platformCode;
        dict["alert"] = alertDict;

        return dict;
    }

    private async Task _PushNotification(
        Notification notification,
        IEnumerable<PushSubscription> subscriptions
    )
    {
        var subList = subscriptions.ToList();
        if (subList.Count == 0) return;

        var requestDict = new Dictionary<string, object>
        {
            ["notifications"] = _BuildNotificationPayload(notification, subList)
        };

        var client = httpFactory.CreateClient();
        client.BaseAddress = _notifyEndpoint;
        var request = await client.PostAsync("/push", new StringContent(
            JsonSerializer.Serialize(requestDict),
            Encoding.UTF8,
            "application/json"
        ));
        request.EnsureSuccessStatusCode();
    }
}