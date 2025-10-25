using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnThinkingSequence : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string? Topic { get; set; }

    public Guid AccountId { get; set; }
}

public enum ThinkingThoughtRole
{
    Assistant,
    User
}

public class SnThinkingThought : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Content { get; set; }

    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Files { get; set; } = [];

    public ThinkingThoughtRole Role { get; set; }

    public Guid SequenceId { get; set; }
    [JsonIgnore] public SnThinkingSequence Sequence { get; set; } = null!;
}