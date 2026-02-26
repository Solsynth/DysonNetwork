using System;
using DysonNetwork.Insight.MiChan;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pgvector.EntityFrameworkCore;

namespace DysonNetwork.Insight.Thought.Memory;

/// <summary>
/// Enhanced memory service with semantic search using EF Core and pgvector.
/// Stores memories in MiChanMemoryRecord format via the main AppDatabase.
/// Provides caching and supports both semantic and chronological retrieval.
/// </summary>
public class MemoryService(
    AppDatabase database,
    EmbeddingService embeddingService,
    ILogger<MemoryService> logger
)
{
    /// <summary>
    /// Store a new interaction with semantic embedding,
    /// the content must be provided by the model and be compacted.
    /// </summary>
    public async Task<MiChanMemoryRecord> StoreMemoryAsync(
        string type,
        string content,
        float? confidence = 0.7f,
        bool hot = false,
        Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        if (!embeddingService.IsAvailable)
            throw new InvalidOperationException("Embedding service is unavailable.");
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        var record = new MiChanMemoryRecord
        {
            Type = type,
            Content = content,
            Embedding = embedding,
            IsHot = hot,
            IsActive = true,
            Confidence = confidence,
            AccountId = accountId,
            LastAccessedAt = SystemClock.Instance.GetCurrentInstant()
        };

        database.MemoryRecords.Add(record);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug(
            "Stored memory {Type} with {ContextId} in database (embedding: {HasEmbedding})",
            type, record.Id, embedding != null
        );

        return record;
    }

    /// <summary>
    /// Search for memories using semantic similarity with optional filters.
    /// Updates LastAccessedAt for returned results.
    /// </summary>
    public async Task<List<MiChanMemoryRecord>> SearchAsync(
        string query,
        string? type = null,
        Guid? accountId = null,
        bool? isHot = null,
        bool? isActive = true,
        float? minConfidence = null,
        int limit = 10,
        double? minSimilarity = null,
        CancellationToken cancellationToken = default)
    {
        if (!embeddingService.IsAvailable)
        {
            logger.LogWarning("Embedding service is unavailable for semantic search");
            return [];
        }

        // Generate query embedding
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        if (queryEmbedding == null)
        {
            logger.LogWarning("Failed to generate query embedding");
            return [];
        }

        var now = SystemClock.Instance.GetCurrentInstant();

        // Build EF Core query with filters
        var queryable = database.MemoryRecords.AsQueryable();

        if (isActive ?? true)
            queryable = queryable.Where(r => r.IsActive);
        if (!string.IsNullOrEmpty(type))
            queryable = queryable.Where(r => r.Type == type);
        if (accountId.HasValue)
            queryable = queryable.Where(r => r.AccountId == accountId);
        if (isHot.HasValue)
            queryable = queryable.Where(r => r.IsHot == isHot);
        if (minConfidence.HasValue)
            queryable = queryable.Where(r => r.Confidence >= minConfidence);

        // Order by cosine distance and select with distance
        var results = await queryable
            .Select(r => new { Record = r, Distance = r.Embedding!.CosineDistance(queryEmbedding) })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Filter by min similarity if specified
        if (minSimilarity.HasValue)
        {
            results = results.Where(x => 1.0 - x.Distance >= minSimilarity.Value).ToList();
        }

        var records = results.Select(x => x.Record).ToList();

        // Update LastAccessedAt for retrieved records
        if (records.Count > 0)
        {
            foreach (var record in records)
            {
                record.LastAccessedAt = now;
            }
            await database.SaveChangesAsync(cancellationToken);
        }

        logger.LogDebug(
            "Found {Count} memories for query (type: {Type}, accountId: {AccountId})",
            records.Count, type, accountId);

        return records;
    }

    /// <summary>
    /// Get a memory by ID. Updates LastAccessedAt on access.
    /// </summary>
    public async Task<MiChanMemoryRecord?> GetAsync(
        Guid id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = database.MemoryRecords.AsQueryable();

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        var record = await query.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record != null)
        {
            // Update LastAccessedAt
            record.LastAccessedAt = SystemClock.Instance.GetCurrentInstant();
            await database.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Retrieved memory {MemoryId}", id);
        }

        return record;
    }

    /// <summary>
    /// Get memories by filters (non-semantic query).
    /// Updates LastAccessedAt for returned results.
    /// </summary>
    public async Task<List<MiChanMemoryRecord>> GetByFiltersAsync(
        string? type = null,
        Guid? accountId = null,
        bool? isHot = null,
        bool? isActive = true,
        float? minConfidence = null,
        int skip = 0,
        int take = 50,
        string? orderBy = "createdAt",
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var query = database.MemoryRecords.AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(type))
            query = query.Where(r => r.Type == type);
        if (accountId.HasValue)
            query = query.Where(r => r.AccountId == accountId);
        if (isHot.HasValue)
            query = query.Where(r => r.IsHot == isHot);
        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive);
        if (minConfidence.HasValue)
            query = query.Where(r => r.Confidence >= minConfidence);

        // Apply ordering
        query = orderBy?.ToLowerInvariant() switch
        {
            "lastaccessedat" => descending
                ? query.OrderByDescending(r => r.LastAccessedAt)
                : query.OrderBy(r => r.LastAccessedAt),
            "confidence" => descending
                ? query.OrderByDescending(r => r.Confidence)
                : query.OrderBy(r => r.Confidence),
            _ => descending
                ? query.OrderByDescending(r => r.CreatedAt)
                : query.OrderBy(r => r.CreatedAt)
        };

        // Apply pagination
        var records = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        // Update LastAccessedAt for retrieved records
        if (records.Count > 0)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            foreach (var record in records)
            {
                record.LastAccessedAt = now;
            }
            await database.SaveChangesAsync(cancellationToken);
        }

        logger.LogDebug(
            "Retrieved {Count} memories by filter (type: {Type}, accountId: {AccountId})",
            records.Count, type, accountId);

        return records;
    }

    /// <summary>
    /// Update a memory. Regenerates embedding if content changes.
    /// </summary>
    public async Task<MiChanMemoryRecord?> UpdateAsync(
        Guid id,
        string? content = null,
        string? type = null,
        float? confidence = null,
        bool? isHot = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var record = await database.MemoryRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive, cancellationToken);

        if (record == null)
        {
            logger.LogWarning("Cannot update non-existent or inactive memory {MemoryId}", id);
            return null;
        }

        // Update fields if provided
        if (content != null && content != record.Content)
        {
            record.Content = content;

            // Regenerate embedding if content changed
            if (embeddingService.IsAvailable)
            {
                var newEmbedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
                if (newEmbedding != null)
                {
                    record.Embedding = newEmbedding;
                }
            }
        }

        if (type != null)
            record.Type = type;
        if (confidence.HasValue)
            record.Confidence = confidence;
        if (isHot.HasValue)
            record.IsHot = isHot.Value;
        if (isActive.HasValue)
            record.IsActive = isActive.Value;

        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Updated memory {MemoryId}", id);

        return record;
    }

    /// <summary>
    /// Delete a memory (sets IsActive to false).
    /// </summary>
    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await database.MemoryRecords
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (record == null)
        {
            logger.LogWarning("Cannot delete non-existent memory {MemoryId}", id);
            return false;
        }

        record.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Deleted memory {MemoryId}", id);

        return true;
    }

    /// <summary>
    /// Get hot memories using semantic search from both global and personal memory DB.
    /// User's hot memory is always loaded regardless of relevance.
    /// </summary>
    public async Task<List<MiChanMemoryRecord>> GetHotMemory(
        Guid? accountId,
        string prompt,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!embeddingService.IsAvailable)
        {
            logger.LogWarning("Embedding service is unavailable for hot memory search");
            return [];
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(prompt, cancellationToken);
        if (queryEmbedding == null)
        {
            logger.LogWarning("Failed to generate query embedding for hot memory search");
            return [];
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var allMemories = new List<(MiChanMemoryRecord Record, double Distance)>();

        var hotQuery = database.MemoryRecords
            .Where(r => r.IsHot && r.IsActive);

        var globalHotMemories = await hotQuery
            .Where(r => r.AccountId == null)
            .Select(r => new { Record = r, Distance = r.Embedding!.CosineDistance(queryEmbedding) })
            .ToListAsync(cancellationToken);

        allMemories.AddRange(globalHotMemories.Select(x => (x.Record, x.Distance)));

        if (accountId.HasValue)
        {
            var personalHotMemories = await hotQuery
                .Where(r => r.AccountId == accountId.Value)
                .Select(r => new { Record = r, Distance = r.Embedding!.CosineDistance(queryEmbedding) })
                .ToListAsync(cancellationToken);

            allMemories.AddRange(personalHotMemories.Select(x => (x.Record, x.Distance)));
        }

        var userHotMemories = allMemories
            .Where(x => x.Record.AccountId == accountId && x.Record.Type == "user")
            .Select(x => x.Record)
            .ToList();

        var otherHotMemories = allMemories
            .Where(x => !(x.Record.AccountId == accountId && x.Record.Type == "user"))
            .OrderBy(x => x.Distance)
            .Take(limit - userHotMemories.Count)
            .Select(x => x.Record)
            .ToList();

        var result = userHotMemories.Concat(otherHotMemories).ToList();

        if (result.Count > 0)
        {
            var recordIds = result.Select(x => x.Id).ToList();
            await database.MemoryRecords
                .Where(r => recordIds.Contains(r.Id))
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastAccessedAt, now), cancellationToken);
        }

        logger.LogDebug(
            "Retrieved {TotalCount} hot memories (user hot: {UserCount}, global+personal: {OtherCount}) for account {AccountId}",
            result.Count, userHotMemories.Count, otherHotMemories.Count, accountId);

        return result;
    }

    /// <summary>
    /// Get random memories from the database to add variety and serendipity.
    /// </summary>
    public async Task<List<MiChanMemoryRecord>> GetRandomMemoriesAsync(
        int limit = 5,
        Guid? excludeAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var query = database.MemoryRecords.AsQueryable();

        query = query.Where(r => r.IsActive);

        if (excludeAccountId.HasValue)
        {
            query = query.Where(r => r.AccountId != excludeAccountId.Value);
        }

        var count = await query.CountAsync(cancellationToken);
        if (count == 0)
            return [];

        var skip = _random.Next(Math.Max(0, count - limit));
        var records = await query
            .OrderBy(r => r.Id)
            .Skip(skip)
            .Take(limit)
            .ToListAsync(cancellationToken);

        logger.LogDebug("Retrieved {Count} random memories (total: {Total})", records.Count, count);
        return records;
    }

    private static readonly Random _random = new();
}
