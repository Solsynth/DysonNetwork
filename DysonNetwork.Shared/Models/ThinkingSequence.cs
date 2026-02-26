using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnThinkingSequence : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string? Topic { get; set; }

    public long TotalToken { get; set; }
    public long PaidToken { get; set; }
    public long FreeTokens { get; set; }

    public bool IsPublic { get; set; } = false;

    public Guid AccountId { get; set; }

    /// <summary>
    /// Indicates if this sequence was initiated by an AI agent (MiChan) rather than the user
    /// </summary>
    public bool AgentInitiated { get; set; } = false;

    /// <summary>
    /// The timestamp when the user last read this conversation
    /// </summary>
    public Instant? UserLastReadAt { get; set; }

    /// <summary>
    /// The timestamp of the last message in this sequence (for sorting)
    /// </summary>
    public Instant LastMessageAt { get; set; }
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
    public Dictionary<string, object>? Metadata { get; set; }
    public List<SnCloudFileReferenceObject>? Files { get; set; }
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

    [Column(TypeName = "jsonb")] public List<SnThinkingMessagePart> Parts { get; set; } = [];

    public ThinkingThoughtRole Role { get; set; }

    public long TokenCount { get; set; }
    [MaxLength(4096)] public string? ModelName { get; set; }
    
    [MaxLength(50)] public string? BotName { get; set; } // "snchan" or "michan"

    public Guid SequenceId { get; set; }
    [JsonIgnore] public SnThinkingSequence Sequence { get; set; } = null!;
}
