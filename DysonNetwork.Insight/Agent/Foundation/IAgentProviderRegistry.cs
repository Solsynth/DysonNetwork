namespace DysonNetwork.Insight.Agent.Foundation;

public interface IAgentProviderRegistry
{
    void Register(string providerId, Func<IAgentProviderAdapter> factory);
    bool TryGetProvider(string providerId, out IAgentProviderAdapter? provider);
    IAgentProviderAdapter GetProvider(string providerId);
    IEnumerable<string> GetAvailableProviders();
}
