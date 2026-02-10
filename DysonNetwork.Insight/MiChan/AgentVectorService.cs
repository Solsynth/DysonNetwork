using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Microsoft.SemanticKernel.Embeddings;
using Npgsql;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Service for managing agent memory using Semantic Kernel's Postgres Vector Store connector.
/// Uses a separate pgvector database for storing and retrieving memories via vector similarity search.
/// </summary>
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0050
public class AgentVectorService : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly ILogger<AgentVectorService> _logger;
    private readonly PostgresVectorStore _vectorStore;
    private const string CollectionName = "agent_memories";
    private bool _disposed;

    [Experimental("SKEXP0020")]
    public AgentVectorService(
        IConfiguration configuration,
        MiChanKernelProvider kernelProvider,
        ILogger<AgentVectorService> logger)
    {
        _kernelProvider = kernelProvider;
        _logger = logger;

        // Build data source with vector support
        var connectionString = configuration.GetConnectionString("AgentVector");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("AgentVector connection string is not configured");
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();

        // Create the vector store
        _vectorStore = new PostgresVectorStore(_dataSource, ownsDataSource: false);
    }

    /// <summary>
    /// Get the embedding service from the kernel
    /// </summary>
    private ITextEmbeddingGenerationService? GetEmbeddingService()
    {
        try
        {
            var kernel = _kernelProvider.GetKernel();
            return kernel.Services.GetService<ITextEmbeddingGenerationService>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding service not available in kernel");
            return null;
        }
    }

    /// <summary>
    /// Get or create the agent memories collection
    /// </summary>
    [Experimental("SKEXP0020")]
    private PostgresCollection<Guid, AgentMemoryRecord> GetCollection()
    {
        return new PostgresCollection<Guid, AgentMemoryRecord>(_dataSource, CollectionName, ownsDataSource: false);
    }

    /// <summary>
    /// Store a new memory with its embedding
    /// </summary>
    public async Task<AgentMemoryRecord> StoreMemoryAsync(
        string agentId,
        string memoryType,
        string content,
        string? contextId = null,
        string? title = null,
        Dictionary<string, object>? metadata = null,
        double importance = 0.5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate embedding for the content
            var embeddingService = GetEmbeddingService();
            if (embeddingService == null)
            {
                _logger.LogError("Embedding service not available");
                throw new InvalidOperationException("Embedding service not available");
            }
            
            var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken: cancellationToken);

            var record = new AgentMemoryRecord
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                MemoryType = memoryType,
                ContextId = contextId,
                Content = content,
                Title = title,
                Metadata = metadata != null ? System.Text.Json.JsonSerializer.Serialize(metadata) : null,
                Importance = importance,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Embedding = embedding
            };

            var collection = GetCollection();
            await collection.UpsertAsync(record, cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Stored memory {MemoryId} for agent {AgentId} of type {MemoryType}",
                record.Id, agentId, memoryType);

            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing memory for agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Search for memories similar to the query
    /// </summary>
    public async Task<List<AgentMemoryRecord>> SearchSimilarMemoriesAsync(
        string query,
        string? agentId = null,
        string? memoryType = null,
        int limit = 10,
        double? minRelevanceScore = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var embeddingService = GetEmbeddingService();
            if (embeddingService == null)
            {
                _logger.LogWarning("Embedding service not available");
                return new List<AgentMemoryRecord>();
            }
            
            var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
            var collection = GetCollection();

            // Build SQL filter based on parameters
            var filters = new List<string>();
            if (!string.IsNullOrEmpty(agentId))
            {
                filters.Add($@"""agent_id"" = '{agentId}'");
            }
            if (!string.IsNullOrEmpty(memoryType))
            {
                filters.Add($@"""memory_type"" = '{memoryType}'");
            }
            filters.Add(@"""is_active"" = true");

            var whereClause = filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : "";
            
            // Perform vector search using raw SQL for more control
            var sql = $@"
                SELECT * FROM ""{CollectionName}"" 
                {whereClause}
                ORDER BY ""embedding"" <=> @queryEmbedding
                LIMIT @limit";

            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("queryEmbedding", queryEmbedding.ToArray());
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<AgentMemoryRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = MapFromReader(reader);
                
                // Calculate similarity score
                var distance = reader.GetDouble(reader.GetOrdinal("distance"));
                var similarity = 1.0 - distance;
                
                if (minRelevanceScore.HasValue && similarity < minRelevanceScore.Value)
                {
                    continue;
                }

                // Update access metrics
                record.LastAccessedAt = DateTime.UtcNow;
                record.AccessCount++;
                await collection.UpsertAsync(record, cancellationToken: cancellationToken);

                results.Add(record);
            }

            _logger.LogDebug(
                "Found {Count} similar memories for query",
                results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching memories for query: {Query}", query);
            return new List<AgentMemoryRecord>();
        }
    }

    /// <summary>
    /// Get memories by context ID
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<List<AgentMemoryRecord>> GetMemoriesByContextAsync(
        string contextId,
        string? agentId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $@"
                SELECT * FROM ""{CollectionName}"" 
                WHERE ""context_id"" = @contextId
                {(!string.IsNullOrEmpty(agentId) ? @$" AND ""agent_id"" = '{agentId}'" : "")}
                AND ""is_active"" = true
                ORDER BY ""created_at"" DESC
                LIMIT @limit";

            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("contextId", contextId);
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<AgentMemoryRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(MapFromReader(reader));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving memories for context {ContextId}", contextId);
            return new List<AgentMemoryRecord>();
        }
    }

    /// <summary>
    /// Get a specific memory by ID
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<AgentMemoryRecord?> GetMemoryAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetCollection();
            var record = await collection.GetAsync(memoryId, cancellationToken: cancellationToken);

            if (record != null)
            {
                // Update access metrics
                record.LastAccessedAt = DateTime.UtcNow;
                record.AccessCount++;
                await collection.UpsertAsync(record, cancellationToken: cancellationToken);
            }

            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving memory {MemoryId}", memoryId);
            return null;
        }
    }

    /// <summary>
    /// Delete a memory by ID
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<bool> DeleteMemoryAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetCollection();
            await collection.DeleteAsync(memoryId, cancellationToken: cancellationToken);
            
            _logger.LogDebug("Deleted memory {MemoryId}", memoryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting memory {MemoryId}", memoryId);
            return false;
        }
    }

    /// <summary>
    /// Archive a memory (soft delete)
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<bool> ArchiveMemoryAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetCollection();
            var record = await collection.GetAsync(memoryId, cancellationToken: cancellationToken);

            if (record == null)
            {
                return false;
            }

            record.IsActive = false;
            await collection.UpsertAsync(record, cancellationToken: cancellationToken);

            _logger.LogDebug("Archived memory {MemoryId}", memoryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving memory {MemoryId}", memoryId);
            return false;
        }
    }

    /// <summary>
    /// Get recent memories for an agent
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<List<AgentMemoryRecord>> GetRecentMemoriesAsync(
        string agentId,
        string? memoryType = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = $@"
                SELECT * FROM ""{CollectionName}"" 
                WHERE ""agent_id"" = @agentId
                {(!string.IsNullOrEmpty(memoryType) ? @$" AND ""memory_type"" = '{memoryType}'" : "")}
                AND ""is_active"" = true
                ORDER BY ""created_at"" DESC
                LIMIT @limit";

            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("agentId", agentId);
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<AgentMemoryRecord>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(MapFromReader(reader));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent memories for agent {AgentId}", agentId);
            return new List<AgentMemoryRecord>();
        }
    }

    /// <summary>
    /// Update memory importance score
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<bool> UpdateImportanceAsync(
        Guid memoryId,
        double newImportance,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetCollection();
            var record = await collection.GetAsync(memoryId, cancellationToken: cancellationToken);

            if (record == null)
            {
                return false;
            }

            record.Importance = Math.Clamp(newImportance, 0.0, 1.0);
            await collection.UpsertAsync(record, cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating importance for memory {MemoryId}", memoryId);
            return false;
        }
    }

    /// <summary>
    /// Delete old memories based on age and low importance
    /// </summary>
    [Experimental("SKEXP0020")]
    public async Task<int> CleanupOldMemoriesAsync(
        TimeSpan maxAge,
        double importanceThreshold = 0.3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            
            var sql = $@"
                DELETE FROM ""{CollectionName}"" 
                WHERE ""created_at"" < @cutoff
                AND ""importance"" < @threshold
                RETURNING ""id""";

            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("cutoff", cutoff);
            cmd.Parameters.AddWithValue("threshold", importanceThreshold);

            var count = 0;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                count++;
            }

            _logger.LogInformation(
                "Cleaned up {Count} old memories (older than {MaxAge} with importance < {Threshold})",
                count, maxAge, importanceThreshold);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old memories");
            return 0;
        }
    }

    /// <summary>
    /// Map database record to AgentMemoryRecord
    /// </summary>
    private AgentMemoryRecord MapFromReader(NpgsqlDataReader reader)
    {
        return new AgentMemoryRecord
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            AgentId = reader.GetString(reader.GetOrdinal("agent_id")),
            MemoryType = reader.GetString(reader.GetOrdinal("memory_type")),
            ContextId = reader.IsDBNull(reader.GetOrdinal("context_id")) ? null : reader.GetString(reader.GetOrdinal("context_id")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title")),
            Metadata = reader.IsDBNull(reader.GetOrdinal("metadata")) ? null : reader.GetString(reader.GetOrdinal("metadata")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            LastAccessedAt = reader.IsDBNull(reader.GetOrdinal("last_accessed_at")) ? null : reader.GetDateTime(reader.GetOrdinal("last_accessed_at")),
            Importance = reader.GetDouble(reader.GetOrdinal("importance")),
            AccessCount = reader.GetInt32(reader.GetOrdinal("access_count")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _dataSource?.Dispose();
            _disposed = true;
        }
    }
}
#pragma warning restore SKEXP0020
#pragma warning restore SKEXP0050
