using Microsoft.SemanticKernel.Embeddings;
using Pgvector;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Service for generating text embeddings using Semantic Kernel
/// </summary>
#pragma warning disable SKEXP0050
public class EmbeddingService
{
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(MiChanKernelProvider kernelProvider, ILogger<EmbeddingService> logger)
    {
        _kernelProvider = kernelProvider;
        _logger = logger;
    }

    private ITextEmbeddingGenerationService? GetEmbeddingService()
    {
        try
        {
            var kernel = _kernelProvider.GetKernel();
            return kernel.Services.GetService<ITextEmbeddingGenerationService>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding service not available. Semantic search will be disabled.");
            return null;
        }
    }

    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    public async Task<Vector?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Cannot generate embedding for empty text");
                return null;
            }

            var embeddingService = GetEmbeddingService();
            if (embeddingService == null)
            {
                return null;
            }

            // Call the service without passing cancellationToken as second parameter
            var embeddings = await embeddingService.GenerateEmbeddingsAsync([text]);
            
            if (embeddings.Count == 0)
            {
                _logger.LogWarning("No embedding generated for text");
                return null;
            }

            var embeddingArray = embeddings[0].ToArray();
            return new Vector(embeddingArray);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {TextPreview}", 
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

            var embeddingService = GetEmbeddingService();
            if (embeddingService == null)
            {
                return texts.Select(_ => (Vector?)null).ToList();
            }

            // Call the service without passing cancellationToken
            var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);
            
            return embeddings.Select(e => (Vector?)new Vector(e.ToArray())).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings for {Count} texts", texts.Count);
            return texts.Select(_ => (Vector?)null).ToList();
        }
    }

    /// <summary>
    /// Check if embedding service is available
    /// </summary>
    public bool IsAvailable => GetEmbeddingService() != null;

    /// <summary>
    /// Extract searchable content from interaction context
    /// </summary>
    public string ExtractSearchableContent(Dictionary<string, object> context)
    {
        var parts = new List<string>();

        // Try to extract meaningful text from various context fields
        if (context.TryGetValue("userMessage", out var userMessage))
        {
            parts.Add($"User: {userMessage}");
        }
        else if (context.TryGetValue("message", out var message))
        {
            parts.Add($"Message: {message}");
        }

        if (context.TryGetValue("aiResponse", out var aiResponse))
        {
            parts.Add($"AI: {aiResponse}");
        }
        else if (context.TryGetValue("response", out var response))
        {
            parts.Add($"Response: {response}");
        }

        if (context.TryGetValue("content", out var content))
        {
            parts.Add($"Content: {content}");
        }

        if (context.TryGetValue("postContent", out var postContent))
        {
            parts.Add($"Post: {postContent}");
        }

        // If we couldn't extract structured content, dump the whole context
        if (parts.Count == 0)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(context);
                // Limit length to avoid huge embeddings
                if (json.Length > 2000)
                {
                    json = json[..2000] + "...";
                }
                return json;
            }
            catch
            {
                return string.Join(" ", context.Select(kv => $"{kv.Key}: {kv.Value}"));
            }
        }

        return string.Join("\n", parts);
    }
}
