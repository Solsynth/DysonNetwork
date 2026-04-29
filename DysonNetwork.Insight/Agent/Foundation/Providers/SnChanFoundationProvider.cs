namespace DysonNetwork.Insight.Agent.Foundation.Providers;

using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Models;

public interface ISnChanFoundationProvider
{
    IAgentProviderAdapter GetChatAdapter(string? modelId = null);
    IAgentProviderAdapter GetVisionAdapter(int? userPerkLevel = null);
    AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true);
    AgentExecutionOptions CreateVisionExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true);
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

    public AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true)
    {
        return new AgentExecutionOptions
        {
            Temperature = temperature ?? _defaultModel.GetEffectiveTemperature(),
            ReasoningEffort = enableThinking ? reasoningEffort ?? _defaultModel.GetEffectiveReasoningEffort() : null,
            EnableThinking = enableThinking,
            EnableTools = _defaultModel.EnableFunctions,
            AutoInvokeTools = false,
            MaxToolRounds = 10
        };
    }

    public IAgentProviderAdapter GetVisionAdapter(int? userPerkLevel = null)
    {
        var serviceId = _configuration.GetValue<string>("SnChan:VisionModel:ModelId")
                        ?? _configuration.GetValue<string>("Thinking:DefaultService")
                        ?? _defaultModel.ModelId;
        var providerId = GetProviderIdFromConfig(serviceId);
        return _providerRegistry.GetProvider(providerId);
    }

    public AgentExecutionOptions CreateVisionExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true)
    {
        var serviceId = _configuration.GetValue<string>("SnChan:VisionModel:ModelId")
                        ?? _configuration.GetValue<string>("Thinking:DefaultService")
                        ?? _defaultModel.ModelId;
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceId}");
        var defaultTemperature = _configuration.GetValue<double?>("SnChan:VisionModel:Temperature")
                                 ?? serviceConfig.GetValue<double?>("Temperature")
                                 ?? _defaultModel.GetEffectiveTemperature();
        var defaultReasoningEffort = enableThinking
            ? reasoningEffort
              ?? _configuration.GetValue<string>("SnChan:VisionModel:ReasoningEffort")
              ?? serviceConfig.GetValue<string>("ReasoningEffort")
            : null;

        return new AgentExecutionOptions
        {
            Temperature = temperature ?? defaultTemperature,
            ReasoningEffort = defaultReasoningEffort,
            EnableThinking = enableThinking,
            EnableTools = false,
            AutoInvokeTools = false,
            MaxToolRounds = 1
        };
    }

    private string GetProviderIdFromConfig(string serviceId)
    {
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceId}");
        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        var apiMode = serviceConfig.GetValue<string>("ApiMode")?.Trim().ToLowerInvariant() ?? "chat";
        var model = serviceConfig.GetValue<string>("Model") ?? serviceId;
        return $"{provider}:{apiMode}:{model}";
    }
}
