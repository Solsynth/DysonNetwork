using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class DeliveryRetryJob(
    AppDatabase db,
    ActivityPubQueueService queueService,
    DeliveryDeadLetterService deadLetterService,
    ILogger<DeliveryRetryJob> logger,
    IClock clock
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = clock.GetCurrentInstant();

        logger.LogInformation("Starting improved delivery retry job");

        try
        {
            var failedDeliveries = await db.ActivityPubDeliveries
                .Where(d => d.Status == DeliveryStatus.Failed && d.NextRetryAt != null && d.NextRetryAt <= now)
                .OrderBy(d => d.NextRetryAt)
                .Take(500)
                .ToListAsync();

            logger.LogInformation("Found {Count} failed deliveries ready for retry", failedDeliveries.Count);

            foreach (var delivery in failedDeliveries)
            {
                if (!DeliveryRetryCalculator.ShouldRetry(delivery.RetryCount))
                {
                    logger.LogWarning("Delivery {DeliveryId} exhausted retries, moving to dead letter queue", delivery.Id);
                    await deadLetterService.MoveToDeadLetterAsync(delivery, "Max retries exceeded");
                    continue;
                }

                var delay = DeliveryRetryCalculator.GetDelayForRetry(delivery.RetryCount);
                var nextRetry = DeliveryRetryCalculator.GetNextRetryTime(delivery.RetryCount);

                Dictionary<string, object> activity;
                try
                {
                    activity = string.IsNullOrEmpty(delivery.ActivityPayload)
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(delivery.ActivityPayload) 
                          ?? new Dictionary<string, object>();
                }
                catch
                {
                    activity = new Dictionary<string, object>();
                    logger.LogWarning("Failed to deserialize activity payload for delivery {DeliveryId}", delivery.Id);
                }

                var message = new ActivityPubDeliveryMessage
                {
                    DeliveryId = delivery.Id,
                    ActivityId = delivery.ActivityId,
                    ActivityType = delivery.ActivityType,
                    Activity = activity,
                    ActorUri = delivery.ActorUri,
                    InboxUri = delivery.InboxUri,
                    CurrentRetry = delivery.RetryCount
                };

                delivery.Status = DeliveryStatus.Pending;
                delivery.NextRetryAt = nextRetry;

                await queueService.EnqueueDeliveryAsync(message);
                logger.LogDebug("Re-enqueued delivery {DeliveryId} for retry {RetryCount} (delay: {Delay})", 
                    delivery.Id, delivery.RetryCount, delay);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Successfully processed {Count} deliveries for retry", failedDeliveries.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing improved delivery retry job");
        }
    }
}

public class DeliveryDeadLetterService(
    AppDatabase db,
    ILogger<DeliveryDeadLetterService> logger
)
{
    public async Task MoveToDeadLetterAsync(SnActivityPubDelivery delivery, string errorMessage)
    {
        var deadLetter = new DeliveryDeadLetter
        {
            DeliveryId = delivery.Id,
            InboxUri = delivery.InboxUri,
            ActorUri = delivery.ActorUri,
            ActivityType = delivery.ActivityType,
            ActivityPayload = delivery.ActivityPayload ?? "{}",
            ErrorMessage = errorMessage,
            RetryCount = delivery.RetryCount,
            CreatedAt = delivery.CreatedAt,
            FailedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.DeliveryDeadLetters.Add(deadLetter);
        db.ActivityPubDeliveries.Remove(delivery);
        await db.SaveChangesAsync();

        logger.LogWarning("Moved delivery {DeliveryId} to dead letter queue: {Error}", delivery.Id, errorMessage);
    }

    public async Task<bool> RetryFromDeadLetterAsync(Guid deadLetterId)
    {
        var deadLetter = await db.DeliveryDeadLetters.FindAsync(deadLetterId);
        if (deadLetter == null)
            return false;

        var delivery = new SnActivityPubDelivery
        {
            ActivityId = $"retry-{Guid.NewGuid()}",
            ActivityType = deadLetter.ActivityType,
            ActivityPayload = deadLetter.ActivityPayload,
            ActorUri = deadLetter.ActorUri,
            InboxUri = deadLetter.InboxUri,
            Status = DeliveryStatus.Pending,
            RetryCount = 0,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.ActivityPubDeliveries.Add(delivery);
        db.DeliveryDeadLetters.Remove(deadLetter);
        await db.SaveChangesAsync();

        logger.LogInformation("Retried delivery {DeadLetterId} as new delivery {DeliveryId}", deadLetterId, delivery.Id);
        return true;
    }

    public async Task<List<DeliveryDeadLetter>> GetDeadLettersAsync(int limit = 100)
    {
        return await db.DeliveryDeadLetters
            .OrderByDescending(d => d.FailedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> PurgeDeadLettersAsync(int olderThanDays = 30)
    {
        var cutoff = SystemClock.Instance.GetCurrentInstant() - Duration.FromDays(olderThanDays);
        
        var oldDeadLetters = await db.DeliveryDeadLetters
            .Where(d => d.FailedAt < cutoff)
            .ToListAsync();

        db.DeliveryDeadLetters.RemoveRange(oldDeadLetters);
        await db.SaveChangesAsync();

        logger.LogInformation("Purged {Count} dead letters older than {Days} days", oldDeadLetters.Count, olderThanDays);
        return oldDeadLetters.Count;
    }

    public async Task<Dictionary<string, int>> GetDeadLetterStatsAsync()
    {
        var stats = await db.DeliveryDeadLetters
            .GroupBy(d => d.ActivityType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        return stats.ToDictionary(x => x.Type, x => x.Count);
    }
}

public class DeliveryBatchProcessingJob(
    AppDatabase db,
    DeliveryBatchService batchService,
    ILogger<DeliveryBatchProcessingJob> logger,
    IClock clock
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var batches = batchService.GetReadyBatches();

        if (batches.Count == 0)
            return;

        logger.LogInformation("Processing {Count} delivery batches", batches.Count);

        foreach (var batch in batches)
        {
            logger.LogDebug("Batch {BatchId}: {Count} items to {Inbox}", 
                batch.Id, batch.Items.Count, batch.InboxUri);
        }
    }
}

public class DeliveryCleanupJob(
    AppDatabase db,
    DeliveryDeadLetterService deadLetterService,
    ILogger<DeliveryCleanupJob> logger,
    IClock clock
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = clock.GetCurrentInstant();
        var cutoffDate = now - Duration.FromDays(7);

        logger.LogInformation("Starting delivery cleanup job");

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

            var purgedDeadLetters = await deadLetterService.PurgeDeadLettersAsync(30);
            if (purgedDeadLetters > 0)
            {
                logger.LogInformation("Purged {Count} old dead letters", purgedDeadLetters);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing delivery cleanup job");
        }
    }
}

public class DeliveryHealthCheckJob(
    AppDatabase db,
    DeliveryMetricsService metricsService,
    ILogger<DeliveryHealthCheckJob> logger,
    IClock clock
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var stats = await metricsService.GetStatisticsAsync();

            logger.LogInformation(
                "Delivery health: Delivered={Delivered}, Failed={Failed}, Pending={Pending}, DeadLetters={DeadLetters}, AvgTime={AvgTime}ms",
                stats.TotalDelivered,
                stats.TotalFailed,
                stats.PendingDelivery,
                stats.DeadLetterCount,
                stats.AverageDeliveryTimeMs);

            if (stats.DeadLetterCount > 100)
            {
                logger.LogWarning("High dead letter count: {Count}", stats.DeadLetterCount);
            }

            var pendingOld = await db.ActivityPubDeliveries
                .CountAsync(d => d.Status == DeliveryStatus.Pending && 
                                 d.CreatedAt < clock.GetCurrentInstant() - Duration.FromHours(1));

            if (pendingOld > 0)
            {
                logger.LogWarning("Found {Count} pending deliveries older than 1 hour", pendingOld);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing delivery health check");
        }
    }
}