using DysonNetwork.Shared.Models;
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
        Exception? exception = null
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
            db.NotificationDeliveryRecords.Add(new SnNotificationDeliveryRecord
            {
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
