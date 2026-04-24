namespace DysonNetwork.Insight.Agent.Foundation;

public abstract record AgentStreamEvent
{
    public record TextDelta(string Delta) : AgentStreamEvent;
    public record ReasoningDelta(string Delta) : AgentStreamEvent;
    public record ToolCallDelta(string ToolCallId, string ToolName, string? ArgumentsDelta) : AgentStreamEvent;
    public record ToolCallStarted(string ToolCallId, string ToolName) : AgentStreamEvent;
    public record ToolCallCompleted(string ToolCallId, string ToolName, string Arguments) : AgentStreamEvent;
    public record ToolResultReady(string ToolCallId, string ToolName, string Result, bool IsError) : AgentStreamEvent;
    public record Completed(AgentFinishReason Reason, int? InputTokens, int? OutputTokens) : AgentStreamEvent;
    public record Error(Exception Exception) : AgentStreamEvent;
}
