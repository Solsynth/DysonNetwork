namespace DysonNetwork.Insight.Agent.Foundation;

using System.ClientModel;
using DysonNetwork.Insight.Agent.Models;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

/// <summary>
/// Factory for creating OpenAI ChatClient instances directly.
/// Replaces Semantic Kernel's kernel creation.
/// </summary>
public class AgentChatClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentChatClientFactory> _logger;

    public AgentChatClientFactory(IConfiguration configuration, ILogger<AgentChatClientFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates a ChatClient for a specific service configured in Thinking:Services
    /// </summary>
    public ChatClient CreateClient(string serviceName)
    {
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceName}");
        var providerType = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var model = serviceConfig.GetValue<string>("Model");
        var endpoint = serviceConfig.GetValue<string>("Endpoint");
        var apiKey = serviceConfig.GetValue<string>("ApiKey");

        if (string.IsNullOrEmpty(providerType))
        {
            throw new InvalidOperationException($"Service '{serviceName}' not found in Thinking:Services configuration.");
        }

        return CreateClient(providerType, model!, endpoint, apiKey);
    }

    /// <summary>
    /// Creates a ChatClient from a ModelConfiguration
    /// </summary>
    public ChatClient CreateClient(ModelConfiguration modelConfig)
    {
        var providerType = modelConfig.GetEffectiveProvider().ToLower();
        var modelName = modelConfig.GetEffectiveModelName();
        var baseUrl = modelConfig.GetEffectiveBaseUrl();
        var apiKey = modelConfig.GetEffectiveApiKey();

        return CreateClient(providerType, modelName, baseUrl, apiKey);
    }

    /// <summary>
    /// Creates a ChatClient with explicit parameters
    /// </summary>
    private ChatClient CreateClient(string providerType, string model, string? endpoint, string? apiKey)
    {
        var clientOptions = new OpenAIClientOptions();

        switch (providerType)
        {
            case "deepseek":
                clientOptions.Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1");
                break;

            case "openrouter":
                clientOptions.Endpoint = new Uri(endpoint ?? "https://openrouter.ai/api/v1");
                break;

            case "aliyun":
                clientOptions.Endpoint = new Uri(endpoint ?? "https://dashscope.aliyuncs.com/compatible-mode/v1");
                _logger.LogInformation("Configured Aliyun DashScope model: {Model}", model);
                break;

            case "bigmodel":
                clientOptions.Endpoint = new Uri(endpoint ?? "https://open.bigmodel.cn/api/paas/v4");
                _logger.LogInformation("Configured BigModel model: {Model}", model);
                break;

            case "longcat":
                clientOptions.Endpoint = new Uri(endpoint ?? "https://api.longcat.chat/openai");
                _logger.LogInformation("Configured Longcat model: {Model}", model);
                break;

            case "ollama":
                clientOptions.Endpoint = new Uri(endpoint ?? "http://localhost:11434/v1");
                break;

            case "custom":
            default:
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new InvalidOperationException(
                        $"Custom provider '{providerType}' requires a BaseUrl/Endpoint.");
                }
                clientOptions.Endpoint = new Uri(endpoint);
                _logger.LogInformation("Configured custom provider '{Provider}' model: {Model} at {Endpoint}",
                    providerType, model, endpoint);
                break;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"Provider '{providerType}' requires an API key.");
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        return openAiClient.GetChatClient(model);
    }

    /// <summary>
    /// Creates an embedding client for the configured embedding service
    /// </summary>
    public EmbeddingClient? CreateEmbeddingClient()
    {
        var embeddingConfig = _configuration.GetSection("Thinking:Embeddings");
        if (!embeddingConfig.Exists())
        {
            embeddingConfig = _configuration.GetSection("Embeddings");
        }
        var modelId = embeddingConfig.GetValue<string>("Model");

        // If Model looks like a service reference, look up in Thinking:Services
        if (!string.IsNullOrEmpty(modelId) && !modelId.Contains('/'))
        {
            return CreateEmbeddingClientFromService(modelId);
        }

        // Fall back to legacy direct configuration
        var provider = embeddingConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        var endpoint = embeddingConfig.GetValue<string>("Endpoint");
        var apiKey = embeddingConfig.GetValue<string>("ApiKey");
        var model = modelId ?? GetDefaultEmbeddingModel(provider);

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Embedding API key not configured. Embeddings will not be available.");
            return null;
        }

        try
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint ?? GetDefaultEndpoint(provider))
            };

            var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
            _logger.LogInformation("Embedding configured with {Provider} model {Model}", provider, model);
            return client.GetEmbeddingClient(model);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not configure embedding service.");
            return null;
        }
    }

    private EmbeddingClient? CreateEmbeddingClientFromService(string serviceId)
    {
        var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceId}");

        if (!serviceConfig.Exists())
        {
            _logger.LogWarning("Embedding service '{ServiceId}' not found in Thinking:Services", serviceId);
            return null;
        }

        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var endpoint = serviceConfig.GetValue<string>("Endpoint");
        var apiKey = serviceConfig.GetValue<string>("ApiKey");
        var model = serviceConfig.GetValue<string>("Model");

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Embedding service '{ServiceId}' has no ApiKey configured", serviceId);
            return null;
        }

        try
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint ?? GetDefaultEndpoint(provider ?? "openrouter"))
            };

            var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
            _logger.LogInformation("Embedding configured from Thinking:Services/{ServiceId}", serviceId);
            return client.GetEmbeddingClient(model ?? GetDefaultEmbeddingModel(provider ?? "openrouter"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not configure embedding service from Thinking:Services/{ServiceId}", serviceId);
            return null;
        }
    }

    private static string GetDefaultEndpoint(string provider) => provider.ToLower() switch
    {
        "openrouter" => "https://openrouter.ai/api/v1",
        "aliyun" => "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "bigmodel" => "https://open.bigmodel.cn/api/paas/v4",
        "deepseek" => "https://api.deepseek.com/v1",
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };

    private static string GetDefaultEmbeddingModel(string provider) => provider.ToLower() switch
    {
        "openrouter" => "qwen/qwen3-embedding-8b",
        "aliyun" => "text-embedding-v3",
        "bigmodel" => "embedding-3",
        "deepseek" => "text-embedding",
        _ => "text-embedding"
    };

    /// <summary>
    /// Checks if a service is configured and available
    /// </summary>
    public bool IsServiceAvailable(string serviceName)
    {
        try
        {
            var serviceConfig = _configuration.GetSection($"Thinking:Services:{serviceName}");
            return !string.IsNullOrEmpty(serviceConfig.GetValue<string>("Model"));
        }
        catch
        {
            return false;
        }
    }
}
