using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring;

public class DeliveryObservabilityService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<DeliveryObservabilityService> logger
)
{
    public async Task RecordEmailAsync(
        string source,
        DeliveryOutcome outcome,
        long durationMilliseconds,
        Exception? exception = null
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            db.EmailDeliveryRecords.Add(new SnEmailDeliveryRecord
            {
                Source = source,
                Provider = "smtp",
                Outcome = outcome,
                DurationMilliseconds = durationMilliseconds,
                Error = exception is null ? null : Truncate(exception.Message),
                CreatedAt = clock.GetCurrentInstant(),
                UpdatedAt = clock.GetCurrentInstant()
            });
            await db.SaveChangesAsync();
        }
        catch (Exception recordException)
        {
            logger.LogError(recordException, "Failed to record email delivery outcome");
        }
    }

    public async Task RecordNotificationAsync(
        SnNotification notification,
        string provider,
        DeliveryOutcome outcome,
        long durationMilliseconds,
        Exception? exception = null,
        Guid? subscriptionId = null
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            db.NotificationDeliveryRecords.Add(new SnNotificationDeliveryRecord
            {
                NotificationId = notification.Id,
                SubscriptionId = subscriptionId,
                Topic = notification.Topic,
                AppId = notification.AppId,
                PushType = notification.PushType,
                Provider = provider,
                Outcome = outcome,
                DurationMilliseconds = durationMilliseconds,
                Error = exception is null ? null : Truncate(exception.Message),
                CreatedAt = clock.GetCurrentInstant(),
                UpdatedAt = clock.GetCurrentInstant()
            });
            await db.SaveChangesAsync();
        }
        catch (Exception recordException)
        {
            logger.LogError(recordException, "Failed to record notification delivery outcome");
        }
    }

    public async Task MarkSopDeliveryReadAsync(Guid subscriptionId, IEnumerable<Guid> notificationIds)
    {
        var ids = notificationIds.ToHashSet();
        if (ids.Count == 0)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            var now = clock.GetCurrentInstant();
            await db.NotificationDeliveryRecords
                .Where(r => r.Provider == "sop")
                .Where(r => r.SubscriptionId == subscriptionId)
                .Where(r => r.Outcome == DeliveryOutcome.Held)
                .Where(r => r.NotificationId != null && ids.Contains(r.NotificationId.Value))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Outcome, DeliveryOutcome.Success)
                    .SetProperty(r => r.UpdatedAt, now));
        }
        catch (Exception recordException)
        {
            logger.LogError(recordException, "Failed to mark SOP notification delivery as read");
        }
    }

    public async Task RecordNotificationSendAsync(SnNotification notification, string source)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            db.NotificationSendRecords.Add(new SnNotificationSendRecord
            {
                Topic = notification.Topic,
                AppId = notification.AppId,
                PushType = notification.PushType,
                Source = source,
                CreatedAt = clock.GetCurrentInstant(),
                UpdatedAt = clock.GetCurrentInstant()
            });
            await db.SaveChangesAsync();
        }
        catch (Exception recordException)
        {
            logger.LogError(recordException, "Failed to record notification send");
        }
    }

    private static string Truncate(string value) => value.Length <= 4096 ? value : value[..4096];
}
