using DysonNetwork.Insight.MiChan;
using Microsoft.Extensions.AI;
using Pgvector;

namespace DysonNetwork.Insight.Thought.Memory;

/// <summary>
/// Service for generating text embeddings using Microsoft.Extensions.AI
/// </summary>
public class EmbeddingService(IKernelProvider kernelProvider, ILogger<EmbeddingService> logger)
{
    /// <summary>
    /// Get the embedding generator from the kernel using the new Microsoft.Extensions.AI interface
    /// </summary>
    #pragma warning disable SKEXP0050
    private IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingGenerator()
    {
        try
        {
            var kernel = kernelProvider.GetKernel();
            return kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding service not available. Semantic search will be disabled.");
            return null;
        }
    }
    #pragma warning restore SKEXP0050

    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    public async Task<Vector?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Cannot generate embedding for empty text");
                return null;
            }

            var embeddingGenerator = GetEmbeddingGenerator();
            if (embeddingGenerator == null)
            {
                return null;
            }

            // Use the new Microsoft.Extensions.AI API
            var result = await embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken);
            
            if (result == null)
            {
                logger.LogWarning("No embedding generated for text");
                return null;
            }

            var embeddingArray = result.Vector.ToArray();
            return new Vector(embeddingArray);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating embedding for text: {TextPreview}", 
                text.Length > 50 ? text[..50] + "..." : text);
            return null;
        }
    }

    /// <summary>
    /// Generate embeddings for multiple texts in batch
    /// </summary>
    public async Task<List<Vector?>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        try
        {
            if (texts.Count == 0)
            {
                return [];
            }

            var embeddingGenerator = GetEmbeddingGenerator();
            if (embeddingGenerator == null)
            {
                return texts.Select(_ => (Vector?)null).ToList();
            }

            // Use the new Microsoft.Extensions.AI API
            var results = new List<Vector?>();
            foreach (var text in texts)
            {
                var result = await embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken);
                if (result != null)
                {
                    results.Add(new Vector(result.Vector.ToArray()));
                }
                else
                {
                    results.Add(null);
                }
            }
            
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating embeddings for {Count} texts", texts.Count);
            return texts.Select(_ => (Vector?)null).ToList();
        }
    }

    /// <summary>
    /// Check if embedding service is available
    /// </summary>
    public bool IsAvailable => GetEmbeddingGenerator() != null;
}
