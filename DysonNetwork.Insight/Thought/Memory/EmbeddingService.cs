using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pgvector;

namespace DysonNetwork.Insight.Thought.Memory;

/// <summary>
/// Service for generating text embeddings using an OpenAI-compatible embeddings endpoint.
/// </summary>
public class EmbeddingService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<EmbeddingService> logger)
{
    private const int DefaultExpectedDimensions = 1536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    public async Task<Vector?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Embedding text was null or empty. Skipping embedding generation.");
                return null;
            }

            var embeddingConfig = ResolveEmbeddingConfig();
            if (embeddingConfig == null)
            {
                logger.LogWarning("Embedding service not available.");
                return null;
            }

            var values = await GenerateEmbeddingValuesAsync(embeddingConfig, text, cancellationToken);
            if (values == null)
            {
                return null;
            }

            if (values.Length != DefaultExpectedDimensions)
            {
                logger.LogWarning(
                    "Embedding dimension mismatch. Expected {Expected}, got {Actual}. Skipping embedding write to avoid DB vector errors.",
                    DefaultExpectedDimensions,
                    values.Length);
                return null;
            }

            return new Vector(values);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate embedding.");
            return null;
        }
    }

    private async Task<float[]?> GenerateEmbeddingValuesAsync(
        EmbeddingProviderConfig config,
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Embedding text was null or empty. Skipping provider call.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEmbeddingsEndpoint(config.Endpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = config.Model,
                input = text
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Embedding provider returned HTTP {StatusCode}: {ResponseBody}",
                (int)response.StatusCode,
                TrimForLog(responseBody));
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0)
            {
                logger.LogWarning(
                    "Embedding provider returned no embedding data: {ResponseBody}",
                    TrimForLog(responseBody));
                return null;
            }

            var first = data[0];
            if (!first.TryGetProperty("embedding", out var embedding) ||
                embedding.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning(
                    "Embedding provider response did not include a float embedding array: {ResponseBody}",
                    TrimForLog(responseBody));
                return null;
            }

            var values = new float[embedding.GetArrayLength()];
            var index = 0;
            foreach (var item in embedding.EnumerateArray())
            {
                values[index++] = item.GetSingle();
            }

            return values;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "Could not parse embedding provider response: {ResponseBody}",
                TrimForLog(responseBody));
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
    public bool IsAvailable => ResolveEmbeddingConfig() != null;

    private EmbeddingProviderConfig? ResolveEmbeddingConfig()
    {
        var embeddingConfig = configuration.GetSection("Thinking:Embeddings");
        if (!embeddingConfig.Exists())
        {
            embeddingConfig = configuration.GetSection("Embeddings");
        }

        var modelId = embeddingConfig.GetValue<string>("Model");
        if (!string.IsNullOrWhiteSpace(modelId) && !modelId.Contains('/'))
        {
            var serviceConfig = configuration.GetSection($"Thinking:Services:{modelId}");
            if (!serviceConfig.Exists())
            {
                logger.LogWarning("Embedding service '{ServiceId}' not found in Thinking:Services", modelId);
                return null;
            }

            var provider = serviceConfig.GetValue<string>("Provider")?.ToLowerInvariant() ?? "openrouter";
            var model = serviceConfig.GetValue<string>("Model");
            var endpoint = serviceConfig.GetValue<string>("Endpoint") ?? GetDefaultEndpoint(provider);
            var apiKey = serviceConfig.GetValue<string>("ApiKey") ?? GetDefaultApiKey(provider);

            return BuildConfig(provider, model, endpoint, apiKey);
        }

        var directProvider = embeddingConfig.GetValue<string>("Provider")?.ToLowerInvariant() ?? "openrouter";
        var directEndpoint = embeddingConfig.GetValue<string>("Endpoint") ?? GetDefaultEndpoint(directProvider);
        var directApiKey = embeddingConfig.GetValue<string>("ApiKey") ?? GetDefaultApiKey(directProvider);
        var directModel = modelId ?? GetDefaultEmbeddingModel(directProvider);

        return BuildConfig(directProvider, directModel, directEndpoint, directApiKey);
    }

    private EmbeddingProviderConfig? BuildConfig(string provider, string? model, string endpoint, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogWarning("Embedding model is not configured.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Embedding API key not configured. Embeddings will not be available.");
            return null;
        }

        return new EmbeddingProviderConfig(provider, model, endpoint, apiKey);
    }

    private string? GetDefaultApiKey(string provider) => provider.ToLowerInvariant() switch
    {
        "openrouter" => configuration.GetValue<string>("Thinking:OpenRouterApiKey"),
        "deepseek" => configuration.GetValue<string>("Thinking:DeepSeekApiKey"),
        "aliyun" => configuration.GetValue<string>("Thinking:AliyunApiKey"),
        "bigmodel" => configuration.GetValue<string>("Thinking:BigModelApiKey"),
        _ => configuration.GetValue<string>($"Thinking:{provider}ApiKey")
    };

    private static string GetDefaultEndpoint(string provider) => provider.ToLowerInvariant() switch
    {
        "openrouter" => "https://openrouter.ai/api/v1",
        "aliyun" => "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "bigmodel" => "https://open.bigmodel.cn/api/paas/v4",
        "deepseek" => "https://api.deepseek.com/v1",
        _ => throw new ArgumentException($"Unknown embedding provider: {provider}")
    };

    private static string GetDefaultEmbeddingModel(string provider) => provider.ToLowerInvariant() switch
    {
        "openrouter" => "openai/text-embedding-3-small",
        "aliyun" => "text-embedding-v3",
        "bigmodel" => "embedding-3",
        "deepseek" => "text-embedding",
        _ => "text-embedding"
    };

    private static Uri BuildEmbeddingsEndpoint(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase)
            ? new Uri(trimmed)
            : new Uri($"{trimmed}/embeddings");
    }

    private static string TrimForLog(string value)
    {
        const int maxLength = 1024;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed record EmbeddingProviderConfig(
        string Provider,
        string Model,
        string Endpoint,
        string ApiKey);
}
