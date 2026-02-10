using System.Text.Json;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Enhanced memory service with semantic search using the AgentVector vector store.
/// Provides caching and delegates persistence to the AgentVectorService.
/// </summary>
#pragma warning disable SKEXP0020
public class MiChanMemoryService
{
    private readonly AgentVectorService _vectorService;
    private readonly ILogger<MiChanMemoryService> _logger;
    private readonly MiChanConfig _config;
    private readonly EmbeddingService _embeddingService;
    private readonly string _agentId;
    
    // In-memory cache for hot data
    private readonly Dictionary<string, List<MiChanInteraction>> _memoryCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public MiChanMemoryService(
        AgentVectorService vectorService, 
        ILogger<MiChanMemoryService> logger, 
        MiChanConfig config,
        EmbeddingService embeddingService)
    {
        _vectorService = vectorService;
        _logger = logger;
        _config = config;
        _embeddingService = embeddingService;
        _agentId = config.BotAccountId ?? "michan-default";
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
            
            // Create the interaction object for in-memory cache
            var interaction = new MiChanInteraction
            {
                Id = Guid.NewGuid(),
                Type = type,
                ContextId = contextId,
                Context = context,
                Memory = memory ?? new Dictionary<string, object>(),
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

            // Persist to vector store if enabled
            if (_config.Memory.PersistToDatabase && !string.IsNullOrWhiteSpace(searchableContent))
            {
                var metadata = new Dictionary<string, object>
                {
                    ["interaction_id"] = interaction.Id,
                    ["type"] = type,
                    ["has_memory"] = memory != null && memory.Count > 0
                };

                await _vectorService.StoreMemoryAsync(
                    agentId: _agentId,
                    memoryType: type,
                    content: searchableContent,
                    contextId: contextId,
                    title: $"{type} - {contextId}",
                    metadata: metadata,
                    importance: 0.7,
                    cancellationToken: cancellationToken
                );
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
            // Search using the vector service
            var memories = await _vectorService.SearchSimilarMemoriesAsync(
                query: query,
                agentId: _agentId,
                limit: limit * 2, // Get more to filter by similarity
                minRelevanceScore: minSimilarity,
                cancellationToken: cancellationToken
            );

            // Convert AgentMemoryRecord back to MiChanInteraction for backward compatibility
            var results = new List<MiChanInteraction>();
            foreach (var memory in memories.Take(limit))
            {
                results.Add(ConvertToInteraction(memory));
            }

            _logger.LogDebug("Found {Count} similar interactions for query", results.Count);
            return results;
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

            // If not in cache, query from vector store
            if (_config.Memory.PersistToDatabase)
            {
                var memories = await _vectorService.GetMemoriesByContextAsync(
                    contextId: contextId,
                    agentId: _agentId,
                    limit: count,
                    cancellationToken: cancellationToken
                );

                return memories
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(ConvertToInteraction)
                    .ToList();
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

        // If we have a query, also get semantically similar memories
        if (!string.IsNullOrWhiteSpace(currentQuery))
        {
            var similar = await _vectorService.SearchSimilarMemoriesAsync(
                query: currentQuery,
                agentId: _agentId,
                limit: semanticCount,
                minRelevanceScore: 0.7,
                cancellationToken: cancellationToken
            );
            
            // Merge results, avoiding duplicates
            var existingIds = results.Select(r => r.Id).ToHashSet();
            foreach (var memory in similar)
            {
                if (!existingIds.Contains(memory.Id))
                {
                    results.Add(ConvertToInteraction(memory));
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
            // Semantic search with type filter
            var memories = await _vectorService.SearchSimilarMemoriesAsync(
                query: semanticQuery,
                agentId: _agentId,
                memoryType: type,
                limit: limit,
                cancellationToken: cancellationToken
            );

            return memories.Select(ConvertToInteraction).ToList();
        }

        // Get recent memories of specific type
        var typedMemories = await _vectorService.GetRecentMemoriesAsync(
            agentId: _agentId,
            memoryType: type,
            limit: limit,
            cancellationToken: cancellationToken
        );

        return typedMemories.Select(ConvertToInteraction).ToList();
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
        var count = await _vectorService.CleanupOldMemoriesAsync(
            maxAge: TimeSpan.FromTicks(maxAge.BclCompatibleTicks),
            importanceThreshold: 0.3,
            cancellationToken: cancellationToken
        );
        
        if (count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old interactions", count);
        }

        return count;
    }

    /// <summary>
    /// Convert AgentMemoryRecord to MiChanInteraction for backward compatibility
    /// </summary>
    private MiChanInteraction ConvertToInteraction(AgentMemoryRecord memory)
    {
        // Try to extract context from metadata if available
        Dictionary<string, object> context = new();
        Dictionary<string, object> memoryData = new();
        
        if (!string.IsNullOrEmpty(memory.Metadata))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(memory.Metadata);
                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        context[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { }
        }

        // Add content to context
        context["content"] = memory.Content;
        if (!string.IsNullOrEmpty(memory.Title))
        {
            context["title"] = memory.Title;
        }

        return new MiChanInteraction
        {
            Id = memory.Id,
            Type = memory.MemoryType,
            ContextId = memory.ContextId ?? "unknown",
            Context = context,
            Memory = memoryData,
            EmbeddedContent = memory.Content,
            CreatedAt = Instant.FromDateTimeUtc(memory.CreatedAt)
        };
    }
}

#pragma warning restore SKEXP0020
