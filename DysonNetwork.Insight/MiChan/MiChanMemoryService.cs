using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.MiChan;

public class MiChanMemoryService
{
    private readonly AppDatabase _db;
    private readonly ILogger<MiChanMemoryService> _logger;
    private readonly MiChanConfig _config;

    // In-memory cache for recent interactions
    private readonly Dictionary<string, List<MiChanInteraction>> _memoryCache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public MiChanMemoryService(AppDatabase db, ILogger<MiChanMemoryService> logger, MiChanConfig config)
    {
        _db = db;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Store a new interaction in memory
    /// </summary>
    public async Task StoreInteractionAsync(string type, string contextId, Dictionary<string, object> context, Dictionary<string, object>? memory = null)
    {
        try
        {
            var interaction = new MiChanInteraction
            {
                Id = Guid.NewGuid(),
                Type = type,
                ContextId = contextId,
                Context = context,
                Memory = memory ?? new Dictionary<string, object>(),
                CreatedAt = DateTime.UtcNow
            };

            // Add to in-memory cache
            await _cacheLock.WaitAsync();
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

            // Persist to database if enabled
            if (_config.Memory.PersistToDatabase)
            {
                _db.MiChanInteractions.Add(interaction);
                await _db.SaveChangesAsync();
            }

            _logger.LogDebug("Stored interaction {Type} for context {ContextId}", type, contextId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing interaction");
        }
    }

    /// <summary>
    /// Get recent interactions for a context
    /// </summary>
    public async Task<List<MiChanInteraction>> GetRecentInteractionsAsync(string contextId, int count = 10)
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_memoryCache.ContainsKey(contextId))
            {
                return _memoryCache[contextId]
                    .OrderByDescending(i => i.CreatedAt)
                    .Take(count)
                    .ToList();
            }

            // If not in cache, try database
            if (_config.Memory.PersistToDatabase)
            {
                return await _db.MiChanInteractions
                    .Where(i => i.ContextId == contextId)
                    .OrderByDescending(i => i.CreatedAt)
                    .Take(count)
                    .ToListAsync();
            }

            return new List<MiChanInteraction>();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Get all memories for a specific type
    /// </summary>
    public async Task<List<MiChanInteraction>> GetInteractionsByTypeAsync(string type, int count = 50)
    {
        if (_config.Memory.PersistToDatabase)
        {
            return await _db.MiChanInteractions
                .Where(i => i.Type == type)
                .OrderByDescending(i => i.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        // Search in cache
        var results = new List<MiChanInteraction>();
        await _cacheLock.WaitAsync();
        try
        {
            foreach (var context in _memoryCache.Values)
            {
                results.AddRange(context.Where(i => i.Type == type));
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        return results.OrderByDescending(i => i.CreatedAt).Take(count).ToList();
    }

    /// <summary>
    /// Extract and store key memory from an interaction
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
                    var latest = _memoryCache[contextId].OrderByDescending(i => i.CreatedAt).First();
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
}
