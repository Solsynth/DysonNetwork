using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Pgvector;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Service for generating text embeddings using Semantic Kernel
/// </summary>
public class EmbeddingService
{
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(Kernel kernel, ILogger<EmbeddingService> logger)
    {
        // Try to get embedding service from kernel
        _embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        _logger = logger;
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

            // Call the service without passing cancellationToken as second parameter
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync([text]);
            
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

            // Call the service without passing cancellationToken
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts);
            
            return embeddings.Select(e => (Vector?)new Vector(e.ToArray())).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings for {Count} texts", texts.Count);
            return texts.Select(_ => (Vector?)null).ToList();
        }
    }

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
