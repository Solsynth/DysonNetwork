namespace DysonNetwork.Insight.Agent.Models;

public class ThinkingConfig
{
    public string DefaultService { get; set; } = "deepseek-chat";
    public string? SystemPromptFile { get; set; }
    public Dictionary<string, ThinkingServiceConfig> Services { get; set; } = new();
}
