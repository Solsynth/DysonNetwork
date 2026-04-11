using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class OutboxBackfillJob(
    AppDatabase db,
    OutboxBackfillService backfillService,
    ILogger<OutboxBackfillJob> logger,
    IClock clock
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting outbox backfill job");

        try
        {
            var now = clock.GetCurrentInstant();
            var backfillThreshold = now - Duration.FromHours(24);

            var actorsNeedingBackfill = await db.FediverseActors
                .Where(a => a.OutboxUri != null && a.OutboxUri != "")
                .Where(a => a.OutboxFetchedAt == null || a.OutboxFetchedAt <= backfillThreshold)
                .OrderBy(a => a.OutboxFetchedAt)
                .Take(50)
                .ToListAsync();

            logger.LogInformation("Found {Count} actors needing outbox backfill", actorsNeedingBackfill.Count);

            foreach (var actor in actorsNeedingBackfill)
            {
                try
                {
                    await backfillService.EnqueueBackfillAsync(actor.Id, actor.Uri, actor.OutboxUri!);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enqueue backfill for actor {ActorUri}", actor.Uri);
                }
            }

            logger.LogInformation("Completed outbox backfill job, enqueued {Count} actors", actorsNeedingBackfill.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing outbox backfill job");
        }
    }
}