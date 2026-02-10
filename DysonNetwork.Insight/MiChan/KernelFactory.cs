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
/// Supports Ollama, DeepSeek, OpenRouter, Aliyun DashScope, and BigModel.
/// </summary>
public class KernelFactory(IConfiguration configuration, ILogger<KernelFactory> logger)
{
    /// <summary>
    /// Creates a kernel for a specific service configured in Thinking:Services
    /// </summary>
    /// <param name="serviceName">The service ID from configuration</param>
    /// <param name="addEmbeddings">Whether to add embedding services (always uses configured embedding provider)</param>
    /// <returns>A configured Kernel instance</returns>
    [Experimental("SKEXP0050")]
    public Kernel CreateKernel(string serviceName, bool addEmbeddings = false)
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
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://open.bigmodel.cn/api/paas/v4/chat/completions") }
                );
                builder.AddOpenAIChatCompletion(model!, bigmodelClient);
                logger.LogInformation("Kernel configured with BigModel model: {Model}", model);
                break;

            default:
                throw new InvalidOperationException($"Unknown provider: {providerType}");
        }

        // Add embedding service - always uses the configured embedding provider (independent of chat provider)
        if (addEmbeddings)
        {
            AddEmbeddingService(builder, providerType);
        }

        return builder.Build();
    }

    /// <summary>
    /// Adds the configured embedding service to the kernel builder.
    /// This is independent of the chat completion provider.
    /// </summary>
    [Experimental("SKEXP0050")]
    private void AddEmbeddingService(IKernelBuilder builder, string chatProviderType)
    {
        var embeddingConfig = configuration.GetSection("Thinking:Embeddings");
        var provider = embeddingConfig.GetValue<string>("Provider")?.ToLower() ?? "openrouter";
        
        logger.LogInformation("Configuring embedding service with provider: {Provider} (chat provider: {ChatProvider})", 
            provider, chatProviderType);

        switch (provider)
        {
            case "ollama":
                var ollamaEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "http://localhost:11434/api";
                var ollamaModel = embeddingConfig.GetValue<string>("Model") 
                    ?? "nomic-embed-text";
                builder.AddOllamaEmbeddingGenerator(
                    ollamaModel,
                    new Uri(ollamaEndpoint)
                );
                logger.LogInformation("Ollama embedding configured with model {Model} at {Endpoint}", 
                    ollamaModel, ollamaEndpoint);
                break;

            case "openrouter":
                var openRouterApiKey = embeddingConfig.GetValue<string>("ApiKey") 
                    ?? configuration.GetValue<string>("Thinking:OpenRouter:ApiKey");
                var openRouterEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? configuration.GetValue<string>("Thinking:OpenRouter:Endpoint") 
                    ?? "https://openrouter.ai/api/v1";
                var openRouterModel = embeddingConfig.GetValue<string>("Model") 
                    ?? configuration.GetValue<string>("Thinking:OpenRouter:EmbeddingModel") 
                    ?? "qwen/qwen3-embedding-8b";

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
                    builder.AddOpenAIEmbeddingGenerator(openRouterModel, openRouterClient, dimensions: 1536);
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
                var aliyunModel = embeddingConfig.GetValue<string>("Model") 
                    ?? "text-embedding-v3";

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
                    builder.AddOpenAIEmbeddingGenerator(aliyunModel, aliyunClient, dimensions: 1536);
                    logger.LogInformation("Aliyun embedding configured with model {Model} for {ChatProvider}", 
                        aliyunModel, chatProviderType);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not configure Aliyun embedding service.");
                }
                break;

            case "openai":
                var openaiApiKey = embeddingConfig.GetValue<string>("ApiKey");
                var openaiEndpoint = embeddingConfig.GetValue<string>("Endpoint") 
                    ?? "https://api.openai.com/v1";
                var openaiModel = embeddingConfig.GetValue<string>("Model") 
                    ?? "text-embedding-3-small";

                if (string.IsNullOrEmpty(openaiApiKey))
                {
                    logger.LogWarning("OpenAI API key not configured. Embeddings will not be available.");
                    break;
                }

                try
                {
                    var openaiClient = new OpenAIClient(
                        new ApiKeyCredential(openaiApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(openaiEndpoint) }
                    );
                    builder.AddOpenAIEmbeddingGenerator(openaiModel, openaiClient, dimensions: 1536);
                    logger.LogInformation("OpenAI embedding configured with model {Model} for {ChatProvider}", 
                        openaiModel, chatProviderType);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not configure OpenAI embedding service.");
                }
                break;

            default:
                logger.LogWarning("Unknown embedding provider: {Provider}. Embeddings will not be available.", provider);
                break;
        }
    }

    /// <summary>
    /// Creates prompt execution settings for a specific service
    /// </summary>
    /// <param name="serviceName">The service ID from configuration</param>
    /// <param name="temperature">Optional temperature override</param>
    /// <returns>Configured PromptExecutionSettings</returns>
    public PromptExecutionSettings CreatePromptExecutionSettings(string serviceName, double? temperature = null)
    {
        var thinkingConfig = configuration.GetSection("Thinking");
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
            "deepseek" or "openrouter" or "aliyun" or "bigmodel" => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                ModelId = serviceName,
                Temperature = (float)temp
            },
            _ => throw new InvalidOperationException($"Unknown provider: {providerType}")
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

#pragma warning restore SKEXP0050
