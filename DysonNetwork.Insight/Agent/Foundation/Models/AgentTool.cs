namespace DysonNetwork.Insight.Agent.Foundation;

public class AgentToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ParametersJsonSchema { get; init; }
    public bool Required { get; init; } = false;
}

public class AgentToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public class AgentToolResult
{
    public string ToolCallId { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public Exception? Exception { get; init; }

    public static AgentToolResult Success(string toolCallId, string toolName, string result) =>
        new() { ToolCallId = toolCallId, ToolName = toolName, Result = result };

    public static AgentToolResult Error(string toolCallId, string toolName, Exception ex) =>
        new() { ToolCallId = toolCallId, ToolName = toolName, Result = ex.Message, IsError = true, Exception = ex };

    public static AgentToolResult Error(string toolCallId, string toolName, string errorMessage) =>
        new() { ToolCallId = toolCallId, ToolName = toolName, Result = errorMessage, IsError = true };
}
