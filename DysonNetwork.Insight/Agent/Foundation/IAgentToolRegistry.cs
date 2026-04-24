namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;

public interface IAgentToolRegistry
{
    void Register(AgentToolDefinition tool, Func<string, Task<string>> executor);
    void Register(string name, string description, Func<string, Task<string>> executor);
    IReadOnlyList<AgentToolDefinition> GetAllDefinitions();
    bool TryGetExecutor(string name, out Func<string, Task<string>>? executor);
}

public interface IAgentToolExecutor
{
    Task<AgentToolResult> ExecuteToolAsync(
        AgentToolCall toolCall,
        CancellationToken cancellationToken = default);
}
