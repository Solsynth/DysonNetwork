using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Enhanced memory service with semantic search using pgvector
/// </summary>
public class MiChanMemoryService
{
    private readonly AppDatabase _db;
    private readonly ILogger<MiChanMemoryService> _logger;
    private readonly MiChanConfig _config;
    private readonly EmbeddingService _embeddingService;
    
    // In-memory cache for hot data
    private readonly Dictionary<string, List<MiChanInteraction>> _memoryCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public MiChanMemoryService(
        AppDatabase db, 
        ILogger<MiChanMemoryService> logger, 
        MiChanConfig config,
        EmbeddingService embeddingService)
    {
        _db = db;
        _logger = logger;
        _config = config;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Store a new interaction with semantic embedding
    /// </summary>
    public async Task<MiChanInteraction> StoreInteractionAsync(
        string type, 
        string contextId, 
        Dictionary<string, object> context, 
        Dictionary<string, object>? memory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract searchable content
            var searchableContent = _embeddingService.ExtractSearchableContent(context);
            
            // Generate embedding
            Vector? embedding = null;
            if (!string.IsNullOrWhiteSpace(searchableContent))
            {
                embedding = await _embeddingService.GenerateEmbeddingAsync(searchableContent, cancellationToken);
            }

            var interaction = new MiChanInteraction
            {
                Id = Guid.NewGuid(),
                Type = type,
                ContextId = contextId,
                Context = context,
                Memory = memory ?? new Dictionary<string, object>(),
                Embedding = embedding,
                EmbeddedContent = searchableContent.Length > 2000 
                    ? searchableContent[..2000] + "..." 
                    : searchableContent,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            // Add to in-memory cache
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (!_memoryCache.ContainsKey(contextId))
                {
                    _memoryCache[contextId] = new List<MiChanInteraction>();
                }
                _memoryCache[contextId].Add(interaction);

                // Trim cache if too large
                if (_memoryCache[contextId].Count > _config.Memory.MaxContextLength)
                {
                    _memoryCache[contextId] = _memoryCache[contextId]
                        .Skip(_memoryCache[contextId].Count - _config.Memory.MaxContextLength)
                        .ToList();
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            // Persist to database
            if (_config.Memory.PersistToDatabase)
            {
                _db.MiChanInteractions.Add(interaction);
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogDebug("Stored interaction {Type} for context {ContextId} with embedding", type, contextId);
            return interaction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing interaction");
            throw;
        }
    }

    /// <summary>
    /// Search for semantically similar interactions across all contexts
    /// </summary>
    public async Task<List<MiChanInteraction>> SearchSimilarInteractionsAsync(
        string query, 
        int limit = 5, 
        double? minSimilarity = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embedding for query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            
            if (queryEmbedding == null)
            {
                _logger.LogWarning("Could not generate embedding for query: {Query}", query);
                return new List<MiChanInteraction>();
            }

            // Perform vector similarity search
            var minDistance = minSimilarity.HasValue 
                ? 1.0 - minSimilarity.Value 
                : (double?)null;

            var results = await _db.MiChanInteractions
                .Where(i => i.Embedding != null)
                .Select(i => new 
                { 
                    Interaction = i, 
                    Distance = i.Embedding!.CosineDistance(queryEmbedding) 
                })
                .Where(x => minDistance == null || x.Distance < minDistance)
                .OrderBy(x => x.Distance)
                .Take(limit)
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Found {Count} similar interactions for query", results.Count);
            
            return results.Select(r => r.Interaction).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching similar interactions");
            return new List<MiChanInteraction>();
        }
    }

    /// <summary>
    /// Get recent interactions for a context (chronological fallback)
    /// </summary>
    public async Task<List<MiChanInteraction>> GetRecentInteractionsAsync(
        string contextId, 
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.ContainsKey(contextId))
            {
                return _memoryCache[contextId]
                    .OrderByDescending(i => i.CreatedAt.ToDateTimeUtc())
                    .Take(count)
                    .ToList();
            }

            // If not in cache, query database
            if (_config.Memory.PersistToDatabase)
            {
                return await _db.MiChanInteractions
                    .AsNoTracking()
                    .Where(i => i.ContextId == contextId)
                    .OrderByDescending(i => i.CreatedAt)
                    .Take(count)
                    .ToListAsync(cancellationToken);
            }

            return new List<MiChanInteraction>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Get relevant context using hybrid search (semantic + recent)
    /// </summary>
    public async Task<List<MiChanInteraction>> GetRelevantContextAsync(
        string contextId,
        string? currentQuery = null,
        int semanticCount = 5,
        int recentCount = 5,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MiChanInteraction>();

        // Get recent interactions from this context
        var recent = await GetRecentInteractionsAsync(contextId, recentCount, cancellationToken);
        results.AddRange(recent);

        // If we have a query, also get semantically similar interactions
        if (!string.IsNullOrWhiteSpace(currentQuery))
        {
            var similar = await SearchSimilarInteractionsAsync(
                currentQuery, 
                semanticCount, 
                minSimilarity: 0.7,
                cancellationToken);
            
            // Merge results, avoiding duplicates
            var existingIds = results.Select(r => r.Id).ToHashSet();
            foreach (var interaction in similar)
            {
                if (!existingIds.Contains(interaction.Id))
                {
                    results.Add(interaction);
                }
            }
        }

        // Sort by recency for coherent context
        return results.OrderByDescending(i => i.CreatedAt.ToDateTimeUtc()).ToList();
    }

    /// <summary>
    /// Search interactions by type with optional semantic filtering
    /// </summary>
    public async Task<List<MiChanInteraction>> GetInteractionsByTypeAsync(
        string type, 
        string? semanticQuery = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(semanticQuery))
        {
            // Semantic + type filter
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(semanticQuery, cancellationToken);
            
            if (queryEmbedding != null)
            {
                return await _db.MiChanInteractions
                    .Where(i => i.Type == type && i.Embedding != null)
                    .Select(i => new 
                    { 
                        Interaction = i, 
                        Distance = i.Embedding!.CosineDistance(queryEmbedding) 
                    })
                    .OrderBy(x => x.Distance)
                    .Take(limit)
                    .Select(x => x.Interaction)
                    .ToListAsync(cancellationToken);
            }
        }

        // Fallback to chronological
        return await _db.MiChanInteractions
            .AsNoTracking()
            .Where(i => i.Type == type)
            .OrderByDescending(i => i.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Store a key-value memory item
    /// </summary>
    public async Task StoreMemoryAsync(string contextId, string key, object value)
    {
        try
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_memoryCache.ContainsKey(contextId) && _memoryCache[contextId].Any())
                {
                    var latest = _memoryCache[contextId].OrderByDescending(i => i.CreatedAt.ToDateTimeUtc()).First();
                    latest.Memory[key] = value;
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            _logger.LogDebug("Stored memory {Key} for context {ContextId}", key, contextId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing memory");
        }
    }

    /// <summary>
    /// Get all active contexts
    /// </summary>
    public async Task<List<string>> GetActiveContextsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            return _memoryCache.Keys.ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Clear cache for a specific context
    /// </summary>
    public async Task ClearContextAsync(string contextId)
    {
        await _cacheLock.WaitAsync();
        try
        {
            _memoryCache.Remove(contextId);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Delete old interactions to manage storage
    /// </summary>
    public async Task<int> CleanupOldInteractionsAsync(Duration maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = SystemClock.Instance.GetCurrentInstant() - maxAge;
        
        var toDelete = await _db.MiChanInteractions
            .Where(i => i.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (toDelete.Count > 0)
        {
            _db.MiChanInteractions.RemoveRange(toDelete);
            await _db.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Cleaned up {Count} old interactions", toDelete.Count);
        }

        return toDelete.Count;
    }
}
