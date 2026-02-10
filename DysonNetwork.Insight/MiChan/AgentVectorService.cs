using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Service for managing agent memory using Semantic Kernel's Postgres Vector Store connector.
/// Uses a separate pgvector database for storing and retrieving memories via vector similarity search.
/// </summary>
#pragma warning disable SKEXP0020
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

        // Ensure table exists
        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureTableExistsAsync()
    {
        try
        {
            var sql = $@"
                CREATE EXTENSION IF NOT EXISTS vector;
                
                CREATE TABLE IF NOT EXISTS ""{CollectionName}"" (
                    id UUID PRIMARY KEY,
                    agent_id TEXT NOT NULL,
                    memory_type TEXT NOT NULL,
                    context_id TEXT,
                    content TEXT NOT NULL,
                    title TEXT,
                    metadata TEXT,
                    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
                    last_accessed_at TIMESTAMP WITH TIME ZONE,
                    importance DOUBLE PRECISION DEFAULT 0.5,
                    access_count INTEGER DEFAULT 0,
                    is_active BOOLEAN DEFAULT true,
                    embedding VECTOR(1536)
                );

                CREATE INDEX IF NOT EXISTS idx_agent_memories_agent_id ON ""{CollectionName}""(agent_id);
                CREATE INDEX IF NOT EXISTS idx_agent_memories_memory_type ON ""{CollectionName}""(memory_type);
                CREATE INDEX IF NOT EXISTS idx_agent_memories_context_id ON ""{CollectionName}""(context_id);
                CREATE INDEX IF NOT EXISTS idx_agent_memories_created_at ON ""{CollectionName}""(created_at);
                
                CREATE INDEX IF NOT EXISTS idx_agent_memories_embedding 
                ON ""{CollectionName}"" USING hnsw (embedding vector_cosine_ops);";

            await using var cmd = _dataSource.CreateCommand(sql);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Agent memories table ensured to exist");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure agent_memories table exists");
        }
    }

    /// <summary>
    /// Get the embedding service from the kernel using the new Microsoft.Extensions.AI interface
    /// </summary>
    #pragma warning disable SKEXP0050
    private IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingService()
    {
        try
        {
            var kernel = _kernelProvider.GetKernel();
            return kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding service not available in kernel");
            return null;
        }
    }
    #pragma warning restore SKEXP0050

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
    public async Task<AgentMemoryRecord?> StoreMemoryAsync(
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
            // Generate embedding for the content (optional)
            var embeddingGenerator = GetEmbeddingService();
            ReadOnlyMemory<float>? embedding = null;
            
            if (embeddingGenerator == null)
            {
                _logger.LogWarning("Embedding service not available, storing memory without embedding");
            }
            else
            {
                try
                {
                    // Use the new Microsoft.Extensions.AI API
                    var result = await embeddingGenerator.GenerateAsync(content, cancellationToken: cancellationToken);
                    if (result != null)
                    {
                        embedding = result.Vector;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate embedding, storing without embedding");
                }
            }

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
                "Stored memory {MemoryId} for agent {AgentId} of type {MemoryType} (embedding: {HasEmbedding})",
                record.Id, agentId, memoryType, embedding.HasValue);

            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing memory for agent {AgentId}", agentId);
            return null;
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
            var embeddingGenerator = GetEmbeddingService();
            if (embeddingGenerator == null)
            {
                _logger.LogWarning("Embedding service not available");
                return new List<AgentMemoryRecord>();
            }
            
            // Use the new Microsoft.Extensions.AI API
            var queryResult = await embeddingGenerator.GenerateAsync(query, cancellationToken: cancellationToken);
            if (queryResult == null)
            {
                _logger.LogWarning("Failed to generate query embedding");
                return new List<AgentMemoryRecord>();
            }
            
            var queryEmbedding = queryResult.Vector;
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
                SELECT *, (""embedding"" <=> @queryEmbedding::vector) as distance
                FROM ""{CollectionName}"" 
                {whereClause}
                ORDER BY distance
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
                var distanceOrdinal = reader.GetOrdinal("distance");
                if (reader.IsDBNull(distanceOrdinal))
                {
                    continue;
                }
                var distance = reader.GetDouble(distanceOrdinal);
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
