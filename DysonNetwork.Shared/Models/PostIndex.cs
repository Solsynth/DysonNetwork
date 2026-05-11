using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;
using Pgvector;

namespace DysonNetwork.Shared.Models;

public class SnPostIndex : ModelBase
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }

    [MaxLength(64)]
    public string SourceHash { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    [Column(TypeName = "vector(1024)")]
    public Vector? Embedding { get; set; }

    public Instant IndexedAt { get; set; }

    public SnPost? Post { get; set; }
}
