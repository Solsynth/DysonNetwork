namespace DysonNetwork.Insight.Agent.Foundation;

using Microsoft.Extensions.Hosting;

public class AgentFoundationInitializationService : IHostedService
{
    private readonly IAgentProviderRegistry _providerRegistry;
    private readonly IAgentProviderAdapter[] _adapters;
    private readonly ILogger<AgentFoundationInitializationService> _logger;

    public AgentFoundationInitializationService(
        IAgentProviderRegistry providerRegistry,
        IAgentProviderAdapter[] adapters,
        ILogger<AgentFoundationInitializationService> logger)
    {
        _providerRegistry = providerRegistry;
        _adapters = adapters;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Agent Foundation with {Count} providers", _adapters.Length);

        foreach (var adapter in _adapters)
        {
            _providerRegistry.Register(adapter.ProviderId, () => adapter);
            _logger.LogInformation("Registered foundation provider: {ProviderId}", adapter.ProviderId);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
