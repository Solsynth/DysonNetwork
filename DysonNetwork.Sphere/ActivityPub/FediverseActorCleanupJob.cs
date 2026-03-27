using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.ActivityPub;

public class FediverseActorCleanupJob(AppDatabase db, ILogger<FediverseActorCleanupJob> logger, IClock clock)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var now = clock.GetCurrentInstant();

        logger.LogInformation("Starting Fediverse actor cleanup job");

        try
        {
            var deletedActorsCount = await DeleteUnusedActorsAsync();
            var deletedInstancesCount = await DeleteOrphanedInstancesAsync();

            logger.LogInformation(
                "Fediverse actor cleanup completed. Deleted {ActorCount} unused actors and {InstanceCount} orphaned instances",
                deletedActorsCount, deletedInstancesCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Fediverse actor cleanup job");
        }
    }

    private async Task<int> DeleteUnusedActorsAsync()
    {
        const int batchSize = 500;
        var totalDeleted = 0;

        while (true)
        {
            var unusedActorIds = await GetUnusedActorIdsAsync(batchSize);
            if (unusedActorIds.Count == 0)
                break;

            var actorsToDelete = await db.FediverseActors
                .Where(a => unusedActorIds.Contains(a.Id))
                .ToListAsync();

            if (actorsToDelete.Count == 0)
                break;

            db.FediverseActors.RemoveRange(actorsToDelete);
            await db.SaveChangesAsync();

            totalDeleted += actorsToDelete.Count;
            logger.LogDebug("Deleted batch of {Count} unused actors", actorsToDelete.Count);
        }

        return totalDeleted;
    }

    private async Task<List<Guid>> GetUnusedActorIdsAsync(int limit)
    {
        var actorIdsWithPosts = await db.Posts
            .Where(p => p.ActorId != null)
            .Select(p => p.ActorId!.Value)
            .Distinct()
            .ToListAsync();

        var actorIdsWithBoosts = await db.Boosts
            .Select(b => b.ActorId)
            .Distinct()
            .ToListAsync();

        var actorIdsWithReactions = await db.PostReactions
            .Where(r => r.ActorId != null)
            .Select(r => r.ActorId!.Value)
            .Distinct()
            .ToListAsync();

        var actorIdsWithFollowing = await db.FediverseRelationships
            .Select(r => r.ActorId)
            .Distinct()
            .ToListAsync();

        var actorIdsWithFollowers = await db.FediverseRelationships
            .Select(r => r.TargetActorId)
            .Distinct()
            .ToListAsync();

        var linkedActorIds = new HashSet<Guid>(
            actorIdsWithPosts
            .Concat(actorIdsWithBoosts)
            .Concat(actorIdsWithReactions)
            .Concat(actorIdsWithFollowing)
            .Concat(actorIdsWithFollowers)
        );

        var unusedActors = await db.FediverseActors
            .Where(a => a.PublisherId == null)
            .Where(a => !linkedActorIds.Contains(a.Id))
            .OrderBy(a => a.CreatedAt)
            .Take(limit)
            .Select(a => a.Id)
            .ToListAsync();

        return unusedActors;
    }

    private async Task<int> DeleteOrphanedInstancesAsync()
    {
        var instanceIdsWithActors = await db.FediverseActors
            .Select(a => a.InstanceId)
            .Distinct()
            .ToListAsync();

        var orphanedInstances = await db.FediverseInstances
            .Where(i => !instanceIdsWithActors.Contains(i.Id))
            .ToListAsync();

        if (orphanedInstances.Count == 0)
            return 0;

        db.FediverseInstances.RemoveRange(orphanedInstances);
        await db.SaveChangesAsync();

        return orphanedInstances.Count;
    }
}
