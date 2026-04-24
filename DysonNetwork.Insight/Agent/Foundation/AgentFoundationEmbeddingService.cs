namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.Agent.Foundation.Models;
using DysonNetwork.Insight.Agent.Models;
using Pgvector;

public class AgentFoundationEmbeddingService
{
    private readonly IAgentProviderRegistry _providerRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentFoundationEmbeddingService> _logger;

    public AgentFoundationEmbeddingService(
        IAgentProviderRegistry providerRegistry,
        IConfiguration configuration,
        ILogger<AgentFoundationEmbeddingService> logger)
    {
        _providerRegistry = providerRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Vector?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Cannot generate embedding for empty text");
                return null;
            }

            var embeddingConfig = _configuration.GetSection("Thinking:Embeddings");
            var modelId = embeddingConfig.GetValue<string>("Model");

            string providerId;
            if (!string.IsNullOrEmpty(modelId) && !modelId.Contains('/'))
            {
                var serviceConfig = _configuration.GetSection($"Thinking:Services:{modelId}");
                var providerName = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
                var model = serviceConfig.GetValue<string>("Model") ?? modelId;
                providerId = $"{providerName}:{model}";
            }
            else
            {
                var providerName = embeddingConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
                var model = modelId ?? "text-embedding-3-small";
                providerId = $"{providerName}:{model}";
            }

            if (!_providerRegistry.TryGetProvider(providerId, out var provider) || provider == null)
            {
                _logger.LogWarning("Embedding provider '{ProviderId}' not available", providerId);
                return null;
            }

            var response = await provider.GenerateEmbeddingAsync(text, cancellationToken);

            return new Vector(response.Embedding.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {TextPreview}",
                text.Length > 50 ? text[..50] + "..." : text);
            return null;
        }
    }

    public async Task<List<Vector?>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<Vector?>();

        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken);
            results.Add(embedding);
        }

        return results;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                var embeddingConfig = _configuration.GetSection("Thinking:Embeddings");
                var modelId = embeddingConfig.GetValue<string>("Model");
                return !string.IsNullOrEmpty(modelId);
            }
            catch
            {
                return false;
            }
        }
    }
}
