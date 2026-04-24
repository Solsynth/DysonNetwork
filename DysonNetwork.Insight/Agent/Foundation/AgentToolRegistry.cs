namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;
using System.Collections.Concurrent;

public class AgentToolRegistry : IAgentToolRegistry, IAgentToolExecutor
{
    private readonly ConcurrentDictionary<string, (AgentToolDefinition Definition, Func<string, Task<string>> Executor)> _tools = new();
    private readonly ILogger<AgentToolRegistry>? _logger;

    public AgentToolRegistry(ILogger<AgentToolRegistry>? logger = null)
    {
        _logger = logger;
    }

    public void Register(AgentToolDefinition tool, Func<string, Task<string>> executor)
    {
        _tools[tool.Name] = (tool, executor);
        _logger?.LogDebug("Registered tool: {ToolName}", tool.Name);
    }

    public void Register(string name, string description, Func<string, Task<string>> executor)
    {
        var tool = new AgentToolDefinition
        {
            Name = name,
            Description = description
        };
        Register(tool, executor);
    }

    public IReadOnlyList<AgentToolDefinition> GetAllDefinitions()
    {
        return _tools.Values.Select(v => v.Definition).ToList();
    }

    public bool TryGetExecutor(string name, out Func<string, Task<string>>? executor)
    {
        if (_tools.TryGetValue(name, out var entry))
        {
            executor = entry.Executor;
            return true;
        }
        executor = null;
        return false;
    }

    public async Task<AgentToolResult> ExecuteToolAsync(AgentToolCall toolCall, CancellationToken cancellationToken = default)
    {
        if (!TryGetExecutor(toolCall.Name, out var executor) || executor == null)
        {
            _logger?.LogWarning("Tool not found: {ToolName}", toolCall.Name);
            return AgentToolResult.Error(toolCall.Id, toolCall.Name, $"Tool '{toolCall.Name}' not found");
        }

        try
        {
            _logger?.LogDebug("Executing tool: {ToolName} with args: {Arguments}", toolCall.Name, toolCall.Arguments);
            var result = await executor(toolCall.Arguments);
            _logger?.LogDebug("Tool {ToolName} executed successfully", toolCall.Name);
            return AgentToolResult.Success(toolCall.Id, toolCall.Name, result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tool {ToolName} execution failed", toolCall.Name);
            return AgentToolResult.Error(toolCall.Id, toolCall.Name, ex);
        }
    }
}
