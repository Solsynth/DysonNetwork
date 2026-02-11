using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using DysonNetwork.Shared.Models;
using NodaTime;
using Pgvector;

namespace DysonNetwork.Insight.MiChan;

[Table("memory_records")]
public class MiChanMemoryRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The hot memory will be provided to the context w/o the tool call if certain condition met.
    /// </summary>
    public bool IsHot { get; set; } = false;

    /// <summary>
    /// Only the active memory will be provided to the model.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The kind of the memory
    /// </summary>
    public string Type { get; set; } = "fact";

    /// <summary>
    /// The content of memory
    /// </summary>
    [Column(TypeName = "text")]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Vector used to search the memory
    /// </summary>
    [Column(TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }

    /// <summary>
    /// A value from 0-1 indicates how this memory close to a fact.
    /// </summary>
    public float? Confidence { get; set; }

    /// <summary>
    /// Used when personal restricted memory database.
    /// If this field is null, the memory is global available.
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>
    /// Last time the memory is accessed by the model.
    /// </summary>
    public Instant? LastAccessedAt { get; set; }

    public string ToPrompt()
    {
        var builder = new StringBuilder();
        builder.Append($"id={Id}; ");
        if (!string.IsNullOrEmpty(Type)) builder.Append($"type={Type}; ");
        if (AccountId.HasValue) builder.Append($"accountId={AccountId}; ");
        if (Confidence.HasValue) builder.Append($"confidence={Confidence}; ");
        builder.Append(Content.Trim());
        return builder.ToString();
    }
}