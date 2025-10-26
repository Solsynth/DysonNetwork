using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnThinkingSequence : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string? Topic { get; set; }
    
    public long TotalToken { get; set; }
    public long PaidToken { get; set; }

    public Guid AccountId { get; set; }
}

public enum ThinkingThoughtRole
{
    Assistant,
    User
}

public enum StreamingContentType
{
    Text,
    Reasoning,
    FunctionCall,
    Unknown
}

public class SnThinkingChunk
{
    public StreamingContentType Type { get; set; }
    public Dictionary<string, object>? Data { get; set; } = new();
}

public class SnThinkingThought : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Content { get; set; }

    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Files { get; set; } = [];
    [Column(TypeName = "jsonb")] public List<SnThinkingChunk> Chunks { get; set; } = [];

    public ThinkingThoughtRole Role { get; set; }

    public long TokenCount { get; set; }
    [MaxLength(4096)] public string? ModelName { get; set; }
    
    public Guid SequenceId { get; set; }
    [JsonIgnore] public SnThinkingSequence Sequence { get; set; } = null!;
}
