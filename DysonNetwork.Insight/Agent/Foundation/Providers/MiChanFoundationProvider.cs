namespace DysonNetwork.Insight.Agent.Foundation.Providers;

using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan;

public interface IMiChanFoundationProvider
{
    IAgentProviderAdapter GetChatAdapter(int? userPerkLevel = null, string? preferredModelId = null);
    IAgentProviderAdapter GetAutonomousAdapter(int? userPerkLevel = null);
    IAgentProviderAdapter GetVisionAdapter(int? userPerkLevel = null);
    IAgentProviderAdapter GetCompactionAdapter(int? userPerkLevel = null);
    AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true);
    AgentExecutionOptions CreateAutonomousExecutionOptions(double? temperature = null, string? reasoningEffort = null);
    AgentExecutionOptions CreateVisionExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true);
}

public class MiChanFoundationProvider : IMiChanFoundationProvider
{
    private readonly IAgentProviderRegistry _providerRegistry;
    private readonly MiChanConfig _config;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MiChanFoundationProvider> _logger;

    public MiChanFoundationProvider(
        IAgentProviderRegistry providerRegistry,
        MiChanConfig config,
        IConfiguration configuration,
        ILogger<MiChanFoundationProvider> logger)
    {
        _providerRegistry = providerRegistry;
        _config = config;
        _configuration = configuration;
        _logger = logger;
    }

    public IAgentProviderAdapter GetChatAdapter(int? userPerkLevel = null, string? preferredModelId = null)
    {
        var modelConfig = _config.ThinkingModel;
        var providerId = !string.IsNullOrWhiteSpace(preferredModelId)
            ? GetProviderIdFromService(preferredModelId)
            : GetProviderId(modelConfig);
        return _providerRegistry.GetProvider(providerId);
    }

    public IAgentProviderAdapter GetAutonomousAdapter(int? userPerkLevel = null)
    {
        var modelConfig = _config.GetAutonomousModel();
        var providerId = GetProviderId(modelConfig);
        return _providerRegistry.GetProvider(providerId);
    }

    public IAgentProviderAdapter GetVisionAdapter(int? userPerkLevel = null)
    {
        var modelConfig = _config.GetVisionModel();
        var providerId = GetProviderId(modelConfig);
        return _providerRegistry.GetProvider(providerId);
    }

    public IAgentProviderAdapter GetCompactionAdapter(int? userPerkLevel = null)
    {
        var modelConfig = _config.GetCompactionModel();
        var providerId = GetProviderId(modelConfig);
        return _providerRegistry.GetProvider(providerId);
    }

    public AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true)
    {
        return new AgentExecutionOptions
        {
            Temperature = temperature ?? _config.ThinkingModel.GetEffectiveTemperature(),
            ReasoningEffort = enableThinking ? reasoningEffort ?? _config.ThinkingModel.GetEffectiveReasoningEffort() : null,
            EnableThinking = enableThinking,
            EnableTools = _config.ThinkingModel.EnableFunctions,
            AutoInvokeTools = false,
            MaxToolRounds = 10
        };
    }

    public AgentExecutionOptions CreateAutonomousExecutionOptions(double? temperature = null, string? reasoningEffort = null)
    {
        var model = _config.GetAutonomousModel();
        return new AgentExecutionOptions
        {
            Temperature = temperature ?? model.GetEffectiveTemperature(),
            ReasoningEffort = reasoningEffort ?? model.GetEffectiveReasoningEffort(),
            EnableTools = model.EnableFunctions,
            AutoInvokeTools = false,
            MaxToolRounds = 10
        };
    }

    public AgentExecutionOptions CreateVisionExecutionOptions(double? temperature = null, string? reasoningEffort = null, bool enableThinking = true)
    {
        var model = _config.GetVisionModel();
        return new AgentExecutionOptions
        {
            Temperature = temperature ?? model.GetEffectiveTemperature(),
            ReasoningEffort = enableThinking ? reasoningEffort ?? model.GetEffectiveReasoningEffort() : null,
            EnableThinking = enableThinking,
            EnableTools = false,
            AutoInvokeTools = false
        };
    }

    private string GetProviderId(ModelConfiguration modelConfig)
    {
        if (string.IsNullOrWhiteSpace(modelConfig.Provider) &&
            string.IsNullOrWhiteSpace(modelConfig.CustomModelName) &&
            string.IsNullOrWhiteSpace(modelConfig.BaseUrl) &&
            string.IsNullOrWhiteSpace(modelConfig.ApiKey))
        {
            var serviceConfig = _configuration.GetSection($"Thinking:Services:{modelConfig.ModelId}");
            if (serviceConfig.Exists())
            {
                return GetProviderIdFromService(modelConfig.ModelId);
            }
        }

        var provider = modelConfig.GetEffectiveProvider().ToLower();
        var apiMode = modelConfig.GetEffectiveApiMode();
        var modelName = modelConfig.GetEffectiveModelName();
        return $"{provider}:{apiMode}:{modelName}";
    }

    private string GetProviderIdFromService(string serviceId)
    {
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceId}");
        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        var apiMode = serviceConfig.GetValue<string>("ApiMode")?.Trim().ToLowerInvariant() ?? "chat";
        var model = serviceConfig.GetValue<string>("Model") ?? serviceId;
        return $"{provider}:{apiMode}:{model}";
    }
}
