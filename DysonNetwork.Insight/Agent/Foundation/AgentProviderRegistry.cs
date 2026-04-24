namespace DysonNetwork.Insight.Agent.Foundation;

using System.Collections.Concurrent;

public class AgentProviderRegistry : IAgentProviderRegistry
{
    private readonly ConcurrentDictionary<string, Func<IAgentProviderAdapter>> _factories = new();
    private readonly ConcurrentDictionary<string, IAgentProviderAdapter> _instances = new();
    private readonly ILogger<AgentProviderRegistry>? _logger;

    public AgentProviderRegistry(ILogger<AgentProviderRegistry>? logger = null)
    {
        _logger = logger;
    }

    public void Register(string providerId, Func<IAgentProviderAdapter> factory)
    {
        _factories[providerId] = factory;
        _logger?.LogDebug("Registered provider factory: {ProviderId}", providerId);
    }

    public bool TryGetProvider(string providerId, out IAgentProviderAdapter? provider)
    {
        if (_instances.TryGetValue(providerId, out var cachedProvider))
        {
            provider = cachedProvider;
            return true;
        }

        if (_factories.TryGetValue(providerId, out var factory))
        {
            provider = factory();
            _instances[providerId] = provider;
            _logger?.LogDebug("Created provider instance: {ProviderId}", providerId);
            return true;
        }

        provider = null;
        return false;
    }

    public IAgentProviderAdapter GetProvider(string providerId)
    {
        if (TryGetProvider(providerId, out var provider) && provider != null)
        {
            return provider;
        }

        throw new InvalidOperationException($"Provider '{providerId}' is not registered. Available providers: {string.Join(", ", GetAvailableProviders())}");
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _factories.Keys;
    }
}
