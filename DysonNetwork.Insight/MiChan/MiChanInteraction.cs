using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;
using Pgvector;

namespace DysonNetwork.Insight.MiChan;

public class MiChanInteraction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!; // 'chat', 'autonomous', 'mention_response', 'admin'
    public string ContextId { get; set; } = null!; // Chat room ID or autonomous session ID
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Context { get; set; } = new();
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Memory { get; set; } = new();
    
    /// <summary>
    /// Vector embedding for semantic search. Stores the embedding of the interaction content.
    /// </summary>
    [Column(TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }
    
    /// <summary>
    /// The text content that was embedded (for reference/debugging)
    /// </summary>
    public string? EmbeddedContent { get; set; }
}
