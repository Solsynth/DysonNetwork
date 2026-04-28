namespace DysonNetwork.Insight.Agent.Models;

public class ThinkingServiceConfig
{
    public string Provider { get; set; } = "openrouter";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public double? Temperature { get; set; }
    public string? ReasoningEffort { get; set; }
    public double BillingMultiplier { get; set; } = 1.0;
    public int PerkLevel { get; set; } = 0;
    public bool SupportsVision { get; set; }
    public bool SupportsReasoning { get; set; }
    public string? DisplayName { get; set; }
}
