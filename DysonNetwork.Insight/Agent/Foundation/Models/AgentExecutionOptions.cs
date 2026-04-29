namespace DysonNetwork.Insight.Agent.Foundation;

public class AgentExecutionOptions
{
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public string? ReasoningEffort { get; init; }
    public bool EnableThinking { get; init; } = true;
    public bool EnableTools { get; init; } = true;
    public bool AutoInvokeTools { get; init; } = false;
    public int MaxToolRounds { get; init; } = 10;
    public IReadOnlyDictionary<string, object>? AdditionalParameters { get; init; }
}
