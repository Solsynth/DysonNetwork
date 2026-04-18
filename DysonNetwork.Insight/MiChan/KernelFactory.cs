using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using DysonNetwork.Insight.Agent.Models;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Factory for creating Semantic Kernel instances with various AI providers.
/// Supports Ollama, DeepSeek, OpenRouter, Aliyun DashScope, BigModel, and custom OpenAI-compatible providers.
/// </summary>
#pragma warning disable SKEXP0010
public class KernelFactory(IConfiguration configuration, ILogger<KernelFactory> logger)
{
    /// <summary>
    /// Creates a kernel for a specific service configured in Thinking:Services
    /// </summary>
    /// <param name="serviceName">The service ID from configuration</param>
    /// <param name="addEmbeddings">Whether to add embedding services (always uses configured embedding provider)</param>
    /// <returns>A configured Kernel instance</returns>
    public Kernel CreateKernel(string serviceName, bool addEmbeddings = true)
    {
        var thinkingConfig = configuration.GetSection("Thinking");
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

        // Add chat completion service based on provider
        AddChatCompletionService(builder, providerType, model, endpoint, apiKey);

        // Add embedding service - always uses the configured embedding provider (independent of chat provider)
        if (addEmbeddings)
            AddEmbeddingService(builder, providerType);

        return builder.Build();
    }

    /// <summary>
    /// Creates a kernel from a ModelConfiguration, supporting custom providers with custom base URLs
    /// </summary>
    /// <param name="modelConfig">The model configuration</param>
    /// <param name="addEmbeddings">Whether to add embedding services</param>
    /// <returns>A configured Kernel instance</returns>
    public Kernel CreateKernel(ModelConfiguration modelConfig, bool addEmbeddings = true)
    {
        var providerType = modelConfig.GetEffectiveProvider().ToLower();
        var modelName = modelConfig.GetEffectiveModelName();
        var baseUrl = modelConfig.GetEffectiveBaseUrl();
        var apiKey = modelConfig.GetEffectiveApiKey();

        var builder = Kernel.CreateBuilder();

        // Add chat completion service
        AddChatCompletionService(builder, providerType, modelName, baseUrl, apiKey);

        // Add embedding service
        if (addEmbeddings)
            AddEmbeddingService(builder, providerType);

        return builder.Build();
    }

    /// <summary>
    /// Adds a chat completion service to the kernel builder based on provider type
    /// </summary>
    private void AddChatCompletionService(
        IKernelBuilder builder,
        string providerType,
        string? model,
        string? endpoint,
        string? apiKey)
    {
        switch (providerType)
        {
            case "ollama":
                builder.AddOllamaChatCompletion(
                    model!,
                    new Uri(endpoint ?? "http://localhost:11434/api")
                );
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
                break;

            case "aliyun":
                var aliyunClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://dashscope.aliyuncs.com/compatible-mode/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, aliyunClient);
                logger.LogInformation("Kernel configured with Aliyun DashScope model: {Model}", model);
                break;

            case "bigmodel":
                var bigmodelClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://open.bigmodel.cn/api/paas/v4") }
                );
                builder.AddOpenAIChatCompletion(model!, bigmodelClient);
                logger.LogInformation("Kernel configured with BigModel model: {Model}", model);
                break;

            case "longcat":
                var longcatClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.longcat.chat/openai") }
                );
                builder.AddOpenAIChatCompletion(model!, longcatClient);
                logger.LogInformation("Kernel configured with Longcat model: {Model}", model);
                break;

            case "custom":
            default:
                // For custom providers or unknown providers, use OpenAI-compatible API
                // This allows any OpenAI-compatible endpoint to be used
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new InvalidOperationException(
                        $"Custom provider '{providerType}' requires a BaseUrl/Endpoint to be specified. " +
                        "Please provide the endpoint in the configuration.");
                }

                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        $"Custom provider '{providerType}' requires an API key. " +
                        "Please provide the ApiKey in the configuration.");
                }

                var customClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
                );
                builder.AddOpenAIChatCompletion(model!, customClient);
                logger.LogInformation("Kernel configured with custom provider '{Provider}' model: {Model} at {Endpoint}",
                    providerType, model, endpoint);
                break;
        }
    }

    /// <summary>
    /// Adds the configured embedding service to the kernel builder.
    /// Supports two configuration modes:
    /// 1. Direct configuration in Thinking:Embeddings with Provider/Endpoint/ApiKey/Model
    /// 2. Reference to Thinking:Services by ModelId (embeddings lookup service details from there)
    /// </summary>
    private void AddEmbeddingService(IKernelBuilder builder, string chatProviderType)
    {
        var embeddingConfig = configuration.GetSection("Thinking:Embeddings");
        var modelId = embeddingConfig.GetValue<string>("Model");
        
        // If Model looks like a service reference (no slashes), look up in Thinking:Services
        if (!string.IsNullOrEmpty(modelId) && !modelId.Contains('/'))
        {
            // Treat as service reference - look up in Thinking:Services
            ConfigureEmbeddingFromService(builder, modelId, chatProviderType);
            return;
        }
        
        // Fall back to legacy direct configuration
        var provider = embeddingConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        
        logger.LogInformation("Configuring embedding service with provider: {Provider} (chat provider: {ChatProvider})", 
            provider, chatProviderType);

        switch (provider)
        {
            case "ollama":
                var ollamaEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "http://localhost:11434/api";
                var ollamaModel = modelId ?? "nomic-embed-text";
                builder.Services.AddOllamaEmbeddingGenerator(
                    endpoint: new Uri(ollamaEndpoint),
                    modelId: ollamaModel
                );
                logger.LogInformation("Ollama embedding configured with model {Model} at {Endpoint}", 
                    ollamaModel, ollamaEndpoint);
                break;

            case "openrouter":
                var openRouterApiKey = embeddingConfig.GetValue<string>("ApiKey");
                var openRouterEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "https://openrouter.ai/api/v1";
                var openRouterModel = modelId ?? "qwen/qwen3-embedding-8b";

                if (string.IsNullOrEmpty(openRouterApiKey))
                {
                    logger.LogWarning("OpenRouter API key not configured. Embeddings will not be available.");
                    break;
                }

                try
                {
                    var openRouterClient = new OpenAIClient(
                        new ApiKeyCredential(openRouterApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) }
                    );
                    builder.Services.AddOpenAIEmbeddingGenerator(
                        modelId: openRouterModel,
                        openAIClient: openRouterClient,
                        dimensions: 1536
                    );
                    logger.LogInformation("OpenRouter embedding configured with model {Model} for {ChatProvider}", 
                        openRouterModel, chatProviderType);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not configure OpenRouter embedding service.");
                }
                break;

            case "aliyun":
                var aliyunApiKey = embeddingConfig.GetValue<string>("ApiKey");
                var aliyunEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "https://dashscope.aliyuncs.com/compatible-mode/v1";
                var aliyunModel = modelId ?? "text-embedding-v3";

                if (string.IsNullOrEmpty(aliyunApiKey))
                {
                    logger.LogWarning("Aliyun API key not configured. Embeddings will not be available.");
                    break;
                }

                try
                {
                    var aliyunClient = new OpenAIClient(
                        new ApiKeyCredential(aliyunApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(aliyunEndpoint) }
                    );
                    builder.Services.AddOpenAIEmbeddingGenerator(
                        modelId: aliyunModel,
                        openAIClient: aliyunClient,
                        dimensions: 1536
                    );
                    logger.LogInformation("Aliyun embedding configured with model {Model} for {ChatProvider}", 
                        aliyunModel, chatProviderType);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not configure Aliyun embedding service.");
                }
                break;

            case "bigmodel":
                var bigmodelApiKey = embeddingConfig.GetValue<string>("ApiKey");
                var bigmodelEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "https://open.bigmodel.cn/api/paas/v4";
                var bigmodelEmbeddingModel = modelId ?? "embedding-3";

                if (string.IsNullOrEmpty(bigmodelApiKey))
                {
                    logger.LogWarning("BigModel API key not configured. Embeddings will not be available.");
                    break;
                }

                try
                {
                    var bigmodelClient = new OpenAIClient(
                        new ApiKeyCredential(bigmodelApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(bigmodelEndpoint) }
                    );
                    builder.Services.AddOpenAIEmbeddingGenerator(
                        modelId: bigmodelEmbeddingModel,
                        openAIClient: bigmodelClient,
                        dimensions: 1536
                    );
                    logger.LogInformation("BigModel embedding configured with model {Model} for {ChatProvider}", 
                        bigmodelEmbeddingModel, chatProviderType);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not configure BigModel embedding service.");
                }
                break;

            default:
                logger.LogWarning("Unknown embedding provider: {Provider}. Embeddings will not be available.", provider);
                break;
        }
    }

    /// <summary>
    /// Configures embedding service by looking up service details from Thinking:Services
    /// </summary>
    private void ConfigureEmbeddingFromService(IKernelBuilder builder, string serviceId, string chatProviderType)
    {
        var serviceConfig = configuration.GetSection($"Thinking:Services:{serviceId}");
        
        if (!serviceConfig.Exists())
        {
            logger.LogWarning("Embedding service '{ServiceId}' not found in Thinking:Services", serviceId);
            return;
        }

        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var endpoint = serviceConfig.GetValue<string>("Endpoint");
        var apiKey = serviceConfig.GetValue<string>("ApiKey");
        var model = serviceConfig.GetValue<string>("Model");

        if (string.IsNullOrEmpty(provider))
        {
            logger.LogWarning("Embedding service '{ServiceId}' has no Provider configured", serviceId);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Embedding service '{ServiceId}' has no ApiKey configured", serviceId);
            return;
        }

        logger.LogInformation("Configuring embedding service from Thinking:Services/{ServiceId} (provider: {Provider}, model: {Model})", 
            serviceId, provider, model);

        try
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? GetDefaultEndpoint(provider)) }
            );

            builder.Services.AddOpenAIEmbeddingGenerator(
                modelId: model ?? GetDefaultEmbeddingModel(provider),
                openAIClient: client,
                dimensions: 1536
            );

            logger.LogInformation("Embedding service configured from Thinking:Services/{ServiceId} for {ChatProvider}", 
                serviceId, chatProviderType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not configure embedding service from Thinking:Services/{ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// Gets the default endpoint for a provider
    /// </summary>
    private static string GetDefaultEndpoint(string provider) => provider.ToLower() switch
    {
        "openrouter" => "https://openrouter.ai/api/v1",
        "aliyun" => "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "bigmodel" => "https://open.bigmodel.cn/api/paas/v4",
        "deepseek" => "https://api.deepseek.com/v1",
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };

    /// <summary>
    /// Gets the default embedding model for a provider
    /// </summary>
    private static string GetDefaultEmbeddingModel(string provider) => provider.ToLower() switch
    {
        "openrouter" => "qwen/qwen3-embedding-8b",
        "aliyun" => "text-embedding-v3",
        "bigmodel" => "embedding-3",
        "deepseek" => "text-embedding",
        _ => "text-embedding"
    };

    /// <summary>
    /// Creates prompt execution settings for a service
    /// </summary>
    public PromptExecutionSettings CreatePromptExecutionSettings(string serviceName, double temperature = 0.7, string? reasoningEffort = null)
    {
        var serviceConfig = configuration.GetSection($"Thinking:Services:{serviceName}");
        var provider = serviceConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        var model = serviceConfig.GetValue<string>("Model") ?? serviceName;

        return provider switch
        {
            "ollama" => new OllamaPromptExecutionSettings
            {
                Temperature = (float)temperature
            },
            _ => new OpenAIPromptExecutionSettings
            {
                ModelId = model,
                Temperature = (float)temperature,
                ReasoningEffort = reasoningEffort
            }
        };
    }

    /// <summary>
    /// Checks if a service is configured and available
    /// </summary>
    public bool IsServiceAvailable(string serviceName)
    {
        try
        {
            var thinkingConfig = configuration.GetSection("Thinking");
            var serviceConfig = thinkingConfig.GetSection($"Services:{serviceName}");
            return !string.IsNullOrEmpty(serviceConfig.GetValue<string>("Model"));
        }
        catch
        {
            return false;
        }
    }
}

#pragma warning restore SKEXP0010

#pragma warning restore SKEXP0050
