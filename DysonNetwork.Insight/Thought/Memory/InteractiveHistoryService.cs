using DysonNetwork.Insight.MiChan;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.Thought.Memory;

/// <summary>
/// Service for tracking MiChan's interaction history with posts and users.
/// Prevents duplicate interactions in autonomous behavior.
/// </summary>
public class InteractiveHistoryService(
    AppDatabase database,
    ILogger<InteractiveHistoryService> logger
)
{
    /// <summary>
    /// Record a new interaction with a resource.
    /// </summary>
    public async Task<MiChanInteractiveHistory> RecordInteractionAsync(
        Guid resourceId,
        string resourceType,
        string behaviour,
        TimeSpan? expiryDuration = null,
        CancellationToken cancellationToken = default)
    {
        var record = new MiChanInteractiveHistory
        {
            ResourceId = resourceId,
            ResourceType = resourceType,
            Behaviour = behaviour,
            IsActive = true,
            ExpiresAt = expiryDuration.HasValue
                ? SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromTimeSpan(expiryDuration.Value))
                : null
        };

        database.InteractiveHistories.Add(record);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Recorded interaction: {Behaviour} on {ResourceType}:{ResourceId}",
            behaviour, resourceType, resourceId);

        return record;
    }

    /// <summary>
    /// Check if MiChan has already interacted with a specific resource.
    /// </summary>
    public async Task<bool> HasInteractedWithAsync(
        Guid resourceId,
        string? resourceType = null,
        string? behaviour = null,
        CancellationToken cancellationToken = default)
    {
        var query = database.InteractiveHistories
            .Where(h => h.ResourceId == resourceId && h.IsActive);

        if (!string.IsNullOrEmpty(resourceType))
            query = query.Where(h => h.ResourceType == resourceType);

        if (!string.IsNullOrEmpty(behaviour))
            query = query.Where(h => h.Behaviour == behaviour);

        // Check for non-expired records only
        var now = SystemClock.Instance.GetCurrentInstant();
        query = query.Where(h => h.ExpiresAt == null || h.ExpiresAt > now);

        var exists = await query.AnyAsync(cancellationToken);

        logger.LogDebug(
            "Interaction check for {ResourceType}:{ResourceId} ({Behaviour}): {Exists}",
            resourceType ?? "any", resourceId, behaviour ?? "any", exists);

        return exists;
    }

    /// <summary>
    /// Get recent interactions with optional filters.
    /// </summary>
    public async Task<List<MiChanInteractiveHistory>> GetRecentInteractionsAsync(
        string? resourceType = null,
        string? behaviour = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = database.InteractiveHistories
            .Where(h => h.IsActive)
            .AsQueryable();

        if (!string.IsNullOrEmpty(resourceType))
            query = query.Where(h => h.ResourceType == resourceType);

        if (!string.IsNullOrEmpty(behaviour))
            query = query.Where(h => h.Behaviour == behaviour);

        // Only include non-expired records
        var now = SystemClock.Instance.GetCurrentInstant();
        query = query.Where(h => h.ExpiresAt == null || h.ExpiresAt > now);

        var records = await query
            .OrderByDescending(h => h.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        logger.LogDebug(
            "Retrieved {Count} recent interactions (type: {ResourceType}, behaviour: {Behaviour})",
            records.Count, resourceType, behaviour);

        return records;
    }

    /// <summary>
    /// Mark an interaction as inactive (soft delete).
    /// </summary>
    public async Task<bool> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await database.InteractiveHistories
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (record == null)
        {
            logger.LogWarning("Cannot deactivate non-existent interaction {InteractionId}", id);
            return false;
        }

        record.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Deactivated interaction {InteractionId}", id);
        return true;
    }

    /// <summary>
    /// Clean up expired interaction records.
    /// </summary>
    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiredRecords = await database.InteractiveHistories
            .Where(h => h.IsActive && h.ExpiresAt != null && h.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredRecords.Count == 0)
            return 0;

        foreach (var record in expiredRecords)
        {
            record.IsActive = false;
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cleaned up {Count} expired interaction records",
            expiredRecords.Count);

        return expiredRecords.Count;
    }

    /// <summary>
    /// Get interaction statistics for a specific resource.
    /// </summary>
    public async Task<Dictionary<string, int>> GetInteractionStatsAsync(
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var interactions = await database.InteractiveHistories
            .Where(h => h.ResourceId == resourceId && h.IsActive)
            .Where(h => h.ExpiresAt == null || h.ExpiresAt > now)
            .GroupBy(h => h.Behaviour)
            .Select(g => new { Behaviour = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return interactions.ToDictionary(x => x.Behaviour, x => x.Count);
    }
}
