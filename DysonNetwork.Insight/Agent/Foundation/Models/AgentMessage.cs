namespace DysonNetwork.Insight.Agent.Foundation.Models;

public class AgentMessage
{
    public AgentMessageRole Role { get; set; }
    public string? Content { get; set; }
    public string? ReasoningContent { get; set; }
    public List<AgentMessageContentPart>? ContentParts { get; set; }
    public string? Name { get; set; }
    public List<AgentToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolResultContent { get; set; }
    public IReadOnlyDictionary<string, object>? Metadata { get; set; }

    public static AgentMessage User(string content, IReadOnlyDictionary<string, object>? metadata = null) =>
        new() { Role = AgentMessageRole.User, Content = content, Metadata = metadata };

    public static AgentMessage Assistant(string content, IReadOnlyDictionary<string, object>? metadata = null) =>
        new() { Role = AgentMessageRole.Assistant, Content = content, Metadata = metadata };

    public static AgentMessage System(string content) =>
        new() { Role = AgentMessageRole.System, Content = content };

    public static AgentMessage FromToolResult(string toolCallId, string result, bool isError = false) =>
        new()
        {
            Role = AgentMessageRole.Tool,
            ToolCallId = toolCallId,
            ToolResultContent = result,
            Metadata = isError ? new Dictionary<string, object> { ["is_error"] = true } : null
        };
}

public enum AgentMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public class AgentMessageContentPart
{
    public AgentContentPartType Type { get; set; }
    public string? Text { get; set; }
    public string? ImageUrl { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageMediaType { get; set; }
    public string? FileUrl { get; set; }
    public byte[]? FileData { get; set; }
    public string? FileMediaType { get; set; }
    public string? FileName { get; set; }
}

public enum AgentContentPartType
{
    Text,
    ImageUrl,
    ImageData,
    FileUrl,
    FileData
}
