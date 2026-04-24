using DysonNetwork.Insight.Agent.Foundation;
using OpenAI.Embeddings;
using Pgvector;

namespace DysonNetwork.Insight.Thought.Memory;

/// <summary>
/// Service for generating text embeddings using OpenAI SDK
/// </summary>
public class EmbeddingService(AgentChatClientFactory chatClientFactory, ILogger<EmbeddingService> logger)
{
    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    public async Task<Vector?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = chatClientFactory.CreateEmbeddingClient();
            if (client == null)
            {
                logger.LogWarning("Embedding service not available.");
                return null;
            }

            var response = await client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
            var vector = response.Value.ToFloats();
            return new Vector(vector.ToArray());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate embedding.");
            return null;
        }
    }

    /// <summary>
    /// Generate embeddings for multiple texts
    /// </summary>
    public async Task<List<Vector?>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Vector?>();
        foreach (var text in texts)
        {
            results.Add(await GenerateEmbeddingAsync(text, cancellationToken));
        }
        return results;
    }

    /// <summary>
    /// Check if embedding service is available
    /// </summary>
    public bool IsAvailable => chatClientFactory.CreateEmbeddingClient() != null;
}
