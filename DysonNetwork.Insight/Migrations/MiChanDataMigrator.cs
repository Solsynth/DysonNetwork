using DysonNetwork.Insight.MiChan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NodaTime;
using Pgvector;
using System.Text.Json;

namespace DysonNetwork.Insight.Migrations;

/// <summary>
/// One-time migration script to migrate MiChan interactions from the main database 
/// to the vector store database.
/// 
/// Usage: Run this manually before applying the EF migration:
///   dotnet run --project DysonNetwork.Insight migrate-michan-data
/// </summary>
public class MiChanDataMigrator
{
    public static async Task MigrateAsync(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<MiChanDataMigrator>();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var appConnectionString = configuration.GetConnectionString("App");
        var agentVectorConnectionString = configuration.GetConnectionString("AgentVector");

        if (string.IsNullOrEmpty(appConnectionString) || string.IsNullOrEmpty(agentVectorConnectionString))
        {
            logger.LogError("Connection strings not configured. Please check App and AgentVector connections.");
            return;
        }

        logger.LogInformation("Starting MiChan data migration...");
        logger.LogInformation("Source: App database");
        logger.LogInformation("Target: AgentVector database");

        // Build data sources
        var appDataSourceBuilder = new NpgsqlDataSourceBuilder(appConnectionString);
        var appDataSource = appDataSourceBuilder.Build();

        var vectorDataSourceBuilder = new NpgsqlDataSourceBuilder(agentVectorConnectionString);
        vectorDataSourceBuilder.UseVector();
        var vectorDataSource = vectorDataSourceBuilder.Build();

        try
        {
            // Ensure target table exists
            await EnsureTargetTableExistsAsync(vectorDataSource, logger);

            // Get count of records to migrate
            var count = await GetRecordCountAsync(appDataSource);
            logger.LogInformation("Found {Count} records to migrate", count);

            if (count == 0)
            {
                logger.LogInformation("No records to migrate.");
                return;
            }

            // Migrate records in batches
            var batchSize = 100;
            var migrated = 0;
            var failed = 0;

            await using var connection = appDataSource.CreateConnection();
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, type, context_id, context, memory, embedded_content, embedding, created_at " +
                "FROM mi_chan_interactions ORDER BY created_at", connection);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                try
                {
                    // Convert Pgvector.Vector to ReadOnlyMemory<float>
                    ReadOnlyMemory<float>? embedding = null;
                    if (!reader.IsDBNull(6))
                    {
                        var vector = reader.GetFieldValue<Vector>(6);
                        if (vector != null)
                        {
                            embedding = new ReadOnlyMemory<float>(vector.ToArray());
                        }
                    }

                    var record = new AgentMemoryRecord
                    {
                        Id = reader.GetGuid(0),
                        AgentId = "michan-migrated",
                        MemoryType = reader.GetString(1),
                        ContextId = reader.GetString(2),
                        Content = reader.IsDBNull(5) ? "" : reader.GetString(5), // embedded_content
                        Metadata = JsonSerializer.Serialize(new
                        {
                            context = reader.IsDBNull(3) ? null : reader.GetFieldValue<Dictionary<string, object>>(3),
                            memory = reader.IsDBNull(4) ? null : reader.GetFieldValue<Dictionary<string, object>>(4),
                            migrated_at = DateTime.UtcNow,
                            source = "legacy_migration"
                        }),
                        CreatedAt = reader.GetFieldValue<DateTime>(7),
                        LastAccessedAt = null,
                        Importance = 0.5,
                        AccessCount = 0,
                        IsActive = true,
                        Embedding = embedding
                    };

                    // Insert into vector store
                    await InsertRecordAsync(vectorDataSource, record);
                    migrated++;

                    if (migrated % batchSize == 0)
                    {
                        logger.LogInformation("Migrated {Migrated}/{Count} records...", migrated, count);
                    }
                }
                catch (Exception ex)
                {
                    var id = reader.GetGuid(0);
                    logger.LogError(ex, "Failed to migrate record {RecordId}", id);
                    failed++;
                }
            }

            logger.LogInformation("Migration complete! Migrated: {Migrated}, Failed: {Failed}", migrated, failed);
        }
        finally
        {
            await appDataSource.DisposeAsync();
            await vectorDataSource.DisposeAsync();
        }
    }

    private static async Task EnsureTargetTableExistsAsync(NpgsqlDataSource dataSource, ILogger<MiChanDataMigrator> logger)
    {
        logger.LogInformation("Ensuring target table exists...");

        var sql = @"
            CREATE TABLE IF NOT EXISTS public.agent_memories (
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

            CREATE INDEX IF NOT EXISTS idx_agent_memories_agent_id ON public.agent_memories(agent_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memories_memory_type ON public.agent_memories(memory_type);
            CREATE INDEX IF NOT EXISTS idx_agent_memories_context_id ON public.agent_memories(context_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memories_created_at ON public.agent_memories(created_at);
            
            CREATE INDEX IF NOT EXISTS idx_agent_memories_embedding 
            ON public.agent_memories USING hnsw (embedding vector_cosine_ops);";

        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();

        logger.LogInformation("Target table ready.");
    }

    private static async Task<int> GetRecordCountAsync(NpgsqlDataSource dataSource)
    {
        await using var cmd = dataSource.CreateCommand("SELECT COUNT(*) FROM mi_chan_interactions");
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task InsertRecordAsync(NpgsqlDataSource dataSource, AgentMemoryRecord record)
    {
        var sql = @"
            INSERT INTO agent_memories (
                id, agent_id, memory_type, context_id, content, title, metadata,
                created_at, last_accessed_at, importance, access_count, is_active, embedding
            ) VALUES (
                @id, @agent_id, @memory_type, @context_id, @content, @title, @metadata,
                @created_at, @last_accessed_at, @importance, @access_count, @is_active, @embedding
            )
            ON CONFLICT (id) DO NOTHING;";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("agent_id", record.AgentId);
        cmd.Parameters.AddWithValue("memory_type", record.MemoryType);
        cmd.Parameters.AddWithValue("context_id", record.ContextId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("content", record.Content);
        cmd.Parameters.AddWithValue("title", record.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", record.Metadata ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", record.CreatedAt);
        cmd.Parameters.AddWithValue("last_accessed_at", record.LastAccessedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("importance", record.Importance);
        cmd.Parameters.AddWithValue("access_count", record.AccessCount);
        cmd.Parameters.AddWithValue("is_active", record.IsActive);
        cmd.Parameters.AddWithValue("embedding", record.Embedding?.ToArray() ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }
}
