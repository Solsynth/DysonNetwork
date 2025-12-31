using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDeliveryRetryJob(AppDatabase db, ActivityPubQueueService queueService, ILogger<ActivityPubDeliveryRetryJob> logger, IClock clock)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = clock.GetCurrentInstant();

        logger.LogInformation("Starting ActivityPub delivery retry job");

        try
        {
            var failedDeliveries = await db.ActivityPubDeliveries
                .Where(d => d.Status == DeliveryStatus.Failed && d.NextRetryAt != null && d.NextRetryAt <= now)
                .OrderBy(d => d.NextRetryAt)
                .Take(100)
                .ToListAsync();

            logger.LogInformation("Found {Count} failed deliveries ready for retry", failedDeliveries.Count);

            foreach (var delivery in failedDeliveries)
            {
                var message = new ActivityPubDeliveryMessage
                {
                    DeliveryId = delivery.Id,
                    ActivityId = delivery.ActivityId,
                    ActivityType = delivery.ActivityType,
                    Activity = new Dictionary<string, object>(),
                    ActorUri = delivery.ActorUri,
                    InboxUri = delivery.InboxUri,
                    CurrentRetry = delivery.RetryCount
                };

                delivery.Status = DeliveryStatus.Pending;
                await queueService.EnqueueDeliveryAsync(message);
                logger.LogDebug("Re-enqueued delivery {DeliveryId} for retry {RetryCount}", delivery.Id, delivery.RetryCount);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Successfully re-enqueued {Count} deliveries for retry", failedDeliveries.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ActivityPub delivery retry job");
        }
    }
}

public class ActivityPubDeliveryCleanupJob(AppDatabase db, ILogger<ActivityPubDeliveryCleanupJob> logger, IClock clock)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = clock.GetCurrentInstant();
        var cutoffDate = now - Duration.FromDays(7);

        logger.LogInformation("Starting ActivityPub delivery cleanup job");

        try
        {
            var oldDeliveries = await db.ActivityPubDeliveries
                .Where(d => 
                    (d.Status == DeliveryStatus.Sent || d.Status == DeliveryStatus.ExhaustedRetries) &&
                    d.CreatedAt < cutoffDate)
                .OrderBy(d => d.CreatedAt)
                .Take(1000)
                .ToListAsync();

            if (oldDeliveries.Count > 0)
            {
                db.ActivityPubDeliveries.RemoveRange(oldDeliveries);
                await db.SaveChangesAsync();
                logger.LogInformation("Cleaned up {Count} old ActivityPub deliveries", oldDeliveries.Count);
            }
            else
            {
                logger.LogInformation("No old deliveries to clean up");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ActivityPub delivery cleanup job");
        }
    }
}
