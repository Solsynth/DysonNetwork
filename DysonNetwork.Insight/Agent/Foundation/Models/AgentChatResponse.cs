namespace DysonNetwork.Insight.Agent.Foundation;

public class AgentChatResponse
{
    public string Content { get; set; } = string.Empty;
    public AgentFinishReason FinishReason { get; set; }
    public List<AgentToolCall>? ToolCalls { get; set; }
    public string? Reasoning { get; set; }
    public IReadOnlyDictionary<string, object>? Metadata { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
}

public enum AgentFinishReason
{
    Stop,
    ToolCalls,
    Length,
    ContentFilter,
    Unknown
}
