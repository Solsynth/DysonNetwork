namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;

public interface IAgentRuntimeSelector
{
    bool UseFoundation { get; }
    IAgentProviderAdapter? GetProviderForModel(string modelId);
    IAgentProviderAdapter? GetDefaultProvider();
    AgentExecutionOptions CreateDefaultExecutionOptions();
}

public class AgentRuntimeSelector : IAgentRuntimeSelector
{
    private readonly IAgentProviderRegistry? _providerRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRuntimeSelector> _logger;

    public bool UseFoundation { get; }

    public AgentRuntimeSelector(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AgentRuntimeSelector> logger)
    {
        _configuration = configuration;
        _logger = logger;
        UseFoundation = configuration.GetValue<bool?>("Thinking:UseFoundation") ?? false;

        if (UseFoundation)
        {
            _providerRegistry = serviceProvider.GetService<IAgentProviderRegistry>();
            _logger.LogInformation("Agent Foundation runtime is ENABLED");
        }
        else
        {
            _logger.LogInformation("Using Semantic Kernel runtime (Foundation disabled)");
        }
    }

    public IAgentProviderAdapter? GetProviderForModel(string modelId)
    {
        if (!UseFoundation || _providerRegistry == null) return null;

        var thinkingConfig = _configuration.GetSection("Thinking");
        var serviceConfig = thinkingConfig.GetSection($"Services:{modelId}");

        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var apiMode = serviceConfig.GetValue<string>("ApiMode")?.Trim().ToLowerInvariant() ?? "chat";
        var model = serviceConfig.GetValue<string>("Model") ?? modelId;

        if (string.IsNullOrEmpty(provider))
        {
            provider = "openrouter";
        }

        var providerId = $"{provider}:{apiMode}:{model}";

        if (_providerRegistry.TryGetProvider(providerId, out var adapter))
        {
            return adapter;
        }

        _logger.LogWarning("Provider not found for model: {ModelId}, tried providerId: {ProviderId}", modelId, providerId);
        return null;
    }

    public IAgentProviderAdapter? GetDefaultProvider()
    {
        if (!UseFoundation || _providerRegistry == null) return null;

        var defaultService = _configuration.GetValue<string>("Thinking:DefaultService");
        if (!string.IsNullOrEmpty(defaultService))
        {
            return GetProviderForModel(defaultService);
        }

        var availableProviders = _providerRegistry.GetAvailableProviders().ToList();
        if (availableProviders.Count > 0)
        {
            var firstProviderId = availableProviders.First();
            if (_providerRegistry.TryGetProvider(firstProviderId, out var adapter))
            {
                return adapter;
            }
        }

        return null;
    }

    public AgentExecutionOptions CreateDefaultExecutionOptions()
    {
        var temp = _configuration.GetValue<double?>("Thinking:DefaultTemperature") ?? 0.7;
        var reasoningEffort = _configuration.GetValue<string?>("Thinking:DefaultReasoningEffort");

        return new AgentExecutionOptions
        {
            Temperature = temp,
            ReasoningEffort = reasoningEffort,
            EnableTools = true,
            AutoInvokeTools = false,
            MaxToolRounds = 10
        };
    }
}
