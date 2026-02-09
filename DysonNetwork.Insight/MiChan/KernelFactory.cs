#pragma warning disable SKEXP0050
using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using OpenAI;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Factory for creating Semantic Kernel instances with various AI providers.
/// Supports Ollama, DeepSeek, OpenRouter, and Aliyun DashScope.
/// </summary>
public class KernelFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KernelFactory> _logger;

    public KernelFactory(IConfiguration configuration, ILogger<KernelFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates a kernel for a specific service configured in Thinking:Services
    /// </summary>
    /// <param name="serviceName">The service ID from configuration</param>
    /// <param name="addEmbeddings">Whether to add embedding services</param>
    /// <returns>A configured Kernel instance</returns>
    [Experimental("SKEXP0050")]
    public Kernel CreateKernel(string serviceName, bool addEmbeddings = false)
    {
        var thinkingConfig = _configuration.GetSection("Thinking");
        var serviceConfig = thinkingConfig.GetSection($"Services:{serviceName}");
        
        var providerType = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var model = serviceConfig.GetValue<string>("Model");
        var endpoint = serviceConfig.GetValue<string>("Endpoint");
        var apiKey = serviceConfig.GetValue<string>("ApiKey");

        if (string.IsNullOrEmpty(providerType))
        {
            throw new InvalidOperationException($"Service '{serviceName}' not found in Thinking:Services configuration.");
        }

        var builder = Kernel.CreateBuilder();

        switch (providerType)
        {
            case "ollama":
                builder.AddOllamaChatCompletion(
                    model!,
                    new Uri(endpoint ?? "http://localhost:11434/api")
                );
                if (addEmbeddings)
                {
                    builder.AddOllamaTextEmbeddingGeneration(
                        "nomic-embed-text",
                        new Uri(endpoint ?? "http://localhost:11434/api")
                    );
                }
                break;

            case "deepseek":
                var deepseekClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, deepseekClient);
                break;

            case "openrouter":
                var openRouterClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://openrouter.ai/api/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, openRouterClient);
                if (addEmbeddings)
                {
                    var embeddingModel = _configuration.GetValue<string>("Thinking:OpenRouter:EmbeddingModel") 
                        ?? "qwen/qwen3-embedding-8b";
                    builder.AddOpenAIEmbeddingGenerator(embeddingModel, openRouterClient, dimensions: 1536);
                    _logger.LogInformation("OpenRouter configured with model {Model} and embedding model {EmbeddingModel}", model, embeddingModel);
                }
                break;

            case "aliyun":
                var aliyunClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://dashscope.aliyuncs.com/compatible-mode/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, aliyunClient);
                _logger.LogInformation("Kernel configured with Aliyun DashScope model: {Model}", model);
                break;

            default:
                throw new InvalidOperationException($"Unknown provider: {providerType}");
        }

        // Try to add OpenRouter embedding service as fallback for providers without native embeddings
        if (addEmbeddings && providerType != "openrouter")
        {
            AddOpenRouterEmbeddingFallback(builder, providerType);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates prompt execution settings for a specific service
    /// </summary>
    /// <param name="serviceName">The service ID from configuration</param>
    /// <param name="temperature">Optional temperature override</param>
    /// <returns>Configured PromptExecutionSettings</returns>
    public PromptExecutionSettings CreatePromptExecutionSettings(string serviceName, double? temperature = null)
    {
        var thinkingConfig = _configuration.GetSection("Thinking");
        var serviceConfig = thinkingConfig.GetSection($"Services:{serviceName}");
        var providerType = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var temp = temperature ?? 0.7;

        return providerType switch
        {
            "ollama" => new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                Temperature = (float)temp
            },
            "deepseek" or "openrouter" or "aliyun" => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                ModelId = serviceName,
                Temperature = (float)temp
            },
            _ => throw new InvalidOperationException($"Unknown provider: {providerType}")
        };
    }

    [Experimental("SKEXP0050")]
    private void AddOpenRouterEmbeddingFallback(IKernelBuilder builder, string? currentProvider)
    {
        var openRouterApiKey = _configuration.GetValue<string>("Thinking:OpenRouter:ApiKey");
        if (string.IsNullOrEmpty(openRouterApiKey))
        {
            _logger.LogWarning("No OpenRouter API key configured for embedding fallback. Semantic search will be disabled for {Provider}.", currentProvider);
            return;
        }

        var openRouterEndpoint = _configuration.GetValue<string>("Thinking:OpenRouter:Endpoint") 
            ?? "https://openrouter.ai/api/v1";
        var embeddingModel = _configuration.GetValue<string>("Thinking:OpenRouter:EmbeddingModel") 
            ?? "qwen/qwen3-embedding-8b";

        try
        {
            var openRouterClient = new OpenAIClient(
                new ApiKeyCredential(openRouterApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) }
            );
            builder.AddOpenAIEmbeddingGenerator(embeddingModel, openRouterClient, dimensions: 1536);
            _logger.LogInformation("OpenRouter embedding fallback configured with model {Model} for {Provider}", 
                embeddingModel, currentProvider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not configure OpenRouter embedding fallback. Semantic search will be disabled.");
        }
    }

    /// <summary>
    /// Checks if a service is configured and available
    /// </summary>
    public bool IsServiceAvailable(string serviceName)
    {
        try
        {
            var thinkingConfig = _configuration.GetSection("Thinking");
            var serviceConfig = thinkingConfig.GetSection($"Services:{serviceName}");
            return !string.IsNullOrEmpty(serviceConfig.GetValue<string>("Model"));
        }
        catch
        {
            return false;
        }
    }
}

#pragma warning restore SKEXP0050
