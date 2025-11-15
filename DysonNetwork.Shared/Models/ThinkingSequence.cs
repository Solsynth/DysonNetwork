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

public enum ThinkingMessagePartType
{
    Text,
    FunctionCall,
    FunctionResult
}

public class SnThinkingMessagePart
{
    public ThinkingMessagePartType Type { get; set; }
    public string? Text { get; set; }
    public SnFunctionCall? FunctionCall { get; set; }
    public SnFunctionResult? FunctionResult { get; set; }
}

public class SnFunctionCall
{
    public string Id { get; set; } = null!;
    public string? PluginName { get; set; }
    public string Name { get; set; } = null!;
    public string Arguments { get; set; } = null!;
}

public class SnFunctionResult
{
    public string CallId { get; set; } = null!;
    public string? PluginName { get; set; }
    public string FunctionName { get; set; } = null!;
    public object Result { get; set; } = null!;
    public bool IsError { get; set; }
}

public class SnThinkingThought : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Files { get; set; } = [];

    [Column(TypeName = "jsonb")] public List<SnThinkingMessagePart> Parts { get; set; } = [];

    public ThinkingThoughtRole Role { get; set; }

    public long TokenCount { get; set; }
    [MaxLength(4096)] public string? ModelName { get; set; }

    public Guid SequenceId { get; set; }
    [JsonIgnore] public SnThinkingSequence Sequence { get; set; } = null!;
}
