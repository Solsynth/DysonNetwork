using Microsoft.Extensions.VectorData;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Vector store record for agent memory using Semantic Kernel Postgres connector.
/// Represents a memory item that can be stored and searched via vector similarity.
/// </summary>
public class AgentMemoryRecord
{
    /// <summary>
    /// Unique identifier for the memory record
    /// </summary>
    [VectorStoreKey(StorageName = "id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The agent or bot identifier that owns this memory
    /// </summary>
    [VectorStoreData(StorageName = "agent_id")]
    public string AgentId { get; set; } = null!;

    /// <summary>
    /// The type of memory (e.g., "conversation", "fact", "preference", "insight")
    /// </summary>
    [VectorStoreData(StorageName = "memory_type")]
    public string MemoryType { get; set; } = null!;

    /// <summary>
    /// Context identifier for grouping related memories (e.g., conversation ID)
    /// </summary>
    [VectorStoreData(StorageName = "context_id")]
    public string? ContextId { get; set; }

    /// <summary>
    /// The actual content of the memory
    /// </summary>
    [VectorStoreData(StorageName = "content")]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Optional title or summary of the memory
    /// </summary>
    [VectorStoreData(StorageName = "title")]
    public string? Title { get; set; }

    /// <summary>
    /// Additional metadata as JSON string
    /// </summary>
    [VectorStoreData(StorageName = "metadata")]
    public string? Metadata { get; set; }

    /// <summary>
    /// Timestamp when the memory was created
    /// </summary>
    [VectorStoreData(StorageName = "created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the memory was last accessed
    /// </summary>
    [VectorStoreData(StorageName = "last_accessed_at")]
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Importance score (0-1) indicating how significant this memory is
    /// </summary>
    [VectorStoreData(StorageName = "importance")]
    public double Importance { get; set; } = 0.5;

    /// <summary>
    /// Access count - how many times this memory has been retrieved
    /// </summary>
    [VectorStoreData(StorageName = "access_count")]
    public int AccessCount { get; set; } = 0;

    /// <summary>
    /// Whether this memory is active or archived
    /// </summary>
    [VectorStoreData(StorageName = "is_active")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The vector embedding of the content for semantic search.
    /// Uses 1536 dimensions (compatible with OpenAI/Qwen embeddings).
    /// </summary>
    [VectorStoreVector(1536, StorageName = "embedding")]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
