using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Insight.SnDoc;

/// <summary>
/// Represents a documentation page in the Solar Network documentation system.
/// </summary>
[Table("sn_doc_pages")]
public class SnDocPage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique URL-friendly identifier for the page.
    /// </summary>
    [MaxLength(255)]
    public string Slug { get; set; } = null!;

    /// <summary>
    /// The title of the documentation page.
    /// </summary>
    [MaxLength(500)]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Short description/summary of the page.
    /// </summary>
    [Column(TypeName = "text")]
    public string? Description { get; set; }

    /// <summary>
    /// Full content of the page (stored as the complete document).
    /// Chunks are stored separately in SnDocChunk.
    /// </summary>
    [Column(TypeName = "text")]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Number of chunks this page is divided into.
    /// </summary>
    public int ChunkCount { get; set; } = 1;

    /// <summary>
    /// Total character length of the content.
    /// </summary>
    public int ContentLength { get; set; }

    /// <summary>
    /// Navigation property for chunks.
    /// </summary>
    public List<SnDocChunk> Chunks { get; set; } = [];
}
