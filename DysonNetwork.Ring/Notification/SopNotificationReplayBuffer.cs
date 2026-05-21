using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Ring.Notification;

public class SopNotificationReplayBuffer(ICacheService cache)
{
    private const int MaxReplayNotifications = 100;
    private static readonly TimeSpan ReplayTtl = TimeSpan.FromDays(30);

    public async Task<List<SnNotification>> GetNotifications(Guid accountId)
    {
        var notifications = await cache.GetData<List<SnNotification>>(GetCacheKey(accountId));
        return notifications ?? [];
    }

    public async Task AppendNotification(SnNotification notification)
    {
        var accountId = notification.AccountId;
        await cache.ExecuteWithLockAsync(
            GetLockKey(accountId),
            async () =>
            {
                var notifications = await GetNotifications(accountId);
                notifications.RemoveAll(n => n.Id == notification.Id);
                notifications.Add(notification);
                notifications = notifications
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(MaxReplayNotifications)
                    .ToList();
                await cache.SetData(GetCacheKey(accountId), notifications, ReplayTtl);
            },
            expiry: TimeSpan.FromSeconds(10),
            waitTime: TimeSpan.FromSeconds(2),
            retryInterval: TimeSpan.FromMilliseconds(100)
        );
    }

    private static string GetCacheKey(Guid accountId) => $"ring:sop:replay:{accountId}";

    private static string GetLockKey(Guid accountId) => $"ring:sop:replay:{accountId}";
}
