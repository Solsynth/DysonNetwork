namespace DysonNetwork.Insight.Agent.Foundation.Providers;

using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Models;

public interface ISnChanFoundationProvider
{
    IAgentProviderAdapter GetChatAdapter(string? modelId = null);
    AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null);
}

public class SnChanFoundationProvider : ISnChanFoundationProvider
{
    private readonly IAgentProviderRegistry _providerRegistry;
    private readonly IConfiguration _configuration;
    private readonly ModelConfiguration _defaultModel;
    private readonly ILogger<SnChanFoundationProvider> _logger;

    public SnChanFoundationProvider(
        IAgentProviderRegistry providerRegistry,
        IConfiguration configuration,
        ILogger<SnChanFoundationProvider> logger)
    {
        _providerRegistry = providerRegistry;
        _configuration = configuration;
        _logger = logger;

        var cfg = configuration.GetSection("Thinking");
        var defaultServiceId = cfg.GetValue<string>("DefaultService") ?? "deepseek-chat";

        _defaultModel = new ModelConfiguration
        {
            ModelId = defaultServiceId,
            Temperature = cfg.GetValue<double?>("DefaultTemperature") ?? 0.7,
            EnableFunctions = true
        };
    }

    public IAgentProviderAdapter GetChatAdapter(string? modelId = null)
    {
        var effectiveModelId = modelId ?? _defaultModel.ModelId;
        var providerId = $"snchan:{effectiveModelId}";

        if (_providerRegistry.TryGetProvider(providerId, out var provider) && provider != null)
        {
            return provider;
        }

        var fallbackProviderId = GetProviderIdFromConfig(effectiveModelId);
        return _providerRegistry.GetProvider(fallbackProviderId);
    }

    public AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null)
    {
        return new AgentExecutionOptions
        {
            Temperature = temperature ?? _defaultModel.GetEffectiveTemperature(),
            ReasoningEffort = reasoningEffort ?? _defaultModel.GetEffectiveReasoningEffort(),
            EnableTools = _defaultModel.EnableFunctions,
            AutoInvokeTools = false,
            MaxToolRounds = 10
        };
    }

    private string GetProviderIdFromConfig(string serviceId)
    {
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceId}");
        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        var model = serviceConfig.GetValue<string>("Model") ?? serviceId;
        return $"{provider}:{model}";
    }
}
