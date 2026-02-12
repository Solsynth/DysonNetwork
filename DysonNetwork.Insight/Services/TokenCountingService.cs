using System.Collections.Concurrent;
using TiktokenSharp;

namespace DysonNetwork.Insight.Services;

/// <summary>
/// Service for accurate token counting using TiktokenSharp.
/// Provides token counting for various AI models including GPT-3.5-turbo and GPT-4.
/// </summary>
public class TokenCountingService(ILogger<TokenCountingService> logger)
{
    private readonly ConcurrentDictionary<string, TikToken> _tokenizers = new();

    // Default encoding for most modern OpenAI models (GPT-4, GPT-3.5-turbo)
    private const string DefaultEncoding = "cl100k_base";

    /// <summary>
    /// Counts tokens in a text string using the specified model's tokenizer.
    /// Falls back to character-based estimation if tokenizer is unavailable.
    /// </summary>
    /// <param name="text">The text to count tokens for</param>
    /// <param name="modelName">Optional model name (defaults to cl100k_base for GPT-3.5/4)</param>
    /// <returns>The number of tokens</returns>
    public int CountTokens(string? text, string? modelName = null)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        try
        {
            var tokenizer = GetTokenizer(modelName);
            return tokenizer.Encode(text).Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to count tokens with TiktokenSharp, falling back to character estimation");
            // Fallback to character-based estimation (roughly 4 characters per token for English)
            return text.Length / 4;
        }
    }

    /// <summary>
    /// Counts tokens in multiple text segments efficiently.
    /// </summary>
    /// <param name="texts">Collection of texts to count</param>
    /// <param name="modelName">Optional model name</param>
    /// <returns>Total token count</returns>
    public int CountTokens(IEnumerable<string?> texts, string? modelName = null)
    {
        var tokenizer = GetTokenizer(modelName);
        var totalTokens = 0;
        
        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    totalTokens += tokenizer.Encode(text).Count;
                }
                catch
                {
                    // Fallback for individual segment
                    totalTokens += text.Length / 4;
                }
            }
        }
        
        return totalTokens;
    }

    /// <summary>
    /// Gets or creates a tokenizer for the specified model.
    /// Uses caching to avoid repeated initialization.
    /// </summary>
    private TikToken GetTokenizer(string? modelName)
    {
        var key = modelName ?? DefaultEncoding;
        
        return _tokenizers.GetOrAdd(key, _ =>
        {
            try
            {
                if (string.IsNullOrEmpty(modelName))
                {
                    return TikToken.GetEncoding(DefaultEncoding);
                }
                
                // Try to get encoding for specific model
                return TikToken.EncodingForModel(modelName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create tokenizer for model {ModelName}, using default", modelName);
                return TikToken.GetEncoding(DefaultEncoding);
            }
        });
    }

    /// <summary>
    /// Estimates the cost based on token count.
    /// </summary>
    /// <param name="tokenCount">Number of tokens</param>
    /// <param name="costPerThousandTokens">Cost per 1000 tokens (varies by model)</param>
    /// <returns>Estimated cost</returns>
    public decimal EstimateCost(int tokenCount, decimal costPerThousandTokens = 0.002m)
    {
        return tokenCount * costPerThousandTokens / 1000m;
    }
}
