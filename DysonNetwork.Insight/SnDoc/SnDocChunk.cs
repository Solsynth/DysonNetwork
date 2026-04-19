using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using Pgvector;

namespace DysonNetwork.Insight.SnDoc;

/// <summary>
/// Represents a chunk of a documentation page used for semantic search.
/// Each page can have multiple chunks for better search granularity.
/// </summary>
[Table("sn_doc_chunks")]
public class SnDocChunk : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the parent page.
    /// </summary>
    public Guid PageId { get; set; }

    /// <summary>
    /// Navigation property to the parent page.
    /// </summary>
    public SnDocPage Page { get; set; } = null!;

    /// <summary>
    /// The index of this chunk within the page (0-based).
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    [Column(TypeName = "text")]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Character offset where this chunk starts in the original content.
    /// </summary>
    public int StartOffset { get; set; }

    /// <summary>
    /// Character offset where this chunk ends in the original content.
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// Vector embedding for semantic search.
    /// Generated from: Title + Description + Chunk Content
    /// </summary>
    [Column(TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Indicates if this is the first chunk (contains the beginning of the document).
    /// Useful for retrieving the most relevant context.
    /// </summary>
    public bool IsFirstChunk { get; set; }
}
