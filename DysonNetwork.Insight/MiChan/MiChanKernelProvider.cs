#pragma warning disable SKEXP0050
using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using OpenAI;

namespace DysonNetwork.Insight.MiChan;

public class MiChanKernelProvider
{
    private readonly MiChanConfig _config;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MiChanKernelProvider> _logger;
    private Kernel? _kernel;

    public MiChanKernelProvider(
        MiChanConfig config,
        IConfiguration configuration,
        ILogger<MiChanKernelProvider> logger)
    {
        _config = config;
        _configuration = configuration;
        _logger = logger;
    }

    [Experimental("SKEXP0050")]
    public Kernel GetKernel()
    {
        if (_kernel != null)
            return _kernel;

        var thinkingConfig = _configuration.GetSection("Thinking");
        var serviceConfig = thinkingConfig.GetSection($"Services:{_config.ThinkingService}");
        
        var providerType = serviceConfig.GetValue<string>("Provider")?.ToLower();
        var model = serviceConfig.GetValue<string>("Model");
        var endpoint = serviceConfig.GetValue<string>("Endpoint");
        var apiKey = serviceConfig.GetValue<string>("ApiKey");

        var builder = Kernel.CreateBuilder();

        switch (providerType)
        {
            case "ollama":
                builder.AddOllamaChatCompletion(
                    model!,
                    new Uri(endpoint ?? "http://localhost:11434/api")
                );
                // Add Ollama embedding service
                builder.AddOllamaTextEmbeddingGeneration(
                    "nomic-embed-text",  // Ollama's good embedding model
                    new Uri(endpoint ?? "http://localhost:11434/api")
                );
                break;
            case "deepseek":
                var deepseekClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, deepseekClient);
                // Note: DeepSeek API does NOT provide embeddings endpoint
                // OpenRouter will be used as fallback for embeddings if configured
                break;
            
            case "openrouter":
                var openRouterClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://openrouter.ai/api/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, openRouterClient);
                // Add OpenRouter embeddings support (e.g., qwen/qwen3-embedding-8b)
                var embeddingModel = _configuration.GetValue<string>("Thinking:OpenRouter:EmbeddingModel") 
                    ?? "qwen/qwen3-embedding-8b";
                builder.AddOpenAITextEmbeddingGeneration(embeddingModel, openRouterClient);
                _logger.LogInformation("OpenRouter configured with model {Model} and embedding model {EmbeddingModel}", model, embeddingModel);
                break;
            default:
                throw new InvalidOperationException($"Unknown thinking provider: {providerType}");
        }

        // Try to add OpenRouter embedding service as fallback for providers without native embeddings
        AddOpenRouterEmbeddingFallback(builder, providerType);

        // Add web search plugins if configured
        InitializeHelperFunctions(builder);

        _kernel = builder.Build();
        return _kernel;
    }

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions(IKernelBuilder builder)
    {
        var bingApiKey = _configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            builder.Plugins.AddFromObject(bing, "bing");
        }

        var googleApiKey = _configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = _configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            builder.Plugins.AddFromObject(google, "google");
        }
    }

    [Experimental("SKEXP0050")]
    private void AddOpenRouterEmbeddingFallback(IKernelBuilder builder, string? currentProvider)
    {
        // Skip if OpenRouter is already the provider (it handles its own embeddings)
        if (currentProvider == "openrouter")
            return;

        // Check if OpenRouter is configured for embedding fallback
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
            // Add OpenRouter embedding service as fallback
            var openRouterClient = new OpenAIClient(
                new ApiKeyCredential(openRouterApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) }
            );
            builder.AddOpenAITextEmbeddingGeneration(embeddingModel, openRouterClient);
            _logger.LogInformation("OpenRouter embedding fallback configured with model {Model} for {Provider}", 
                embeddingModel, currentProvider);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not configure OpenRouter embedding fallback. Semantic search will be disabled.");
        }
    }

    public PromptExecutionSettings CreatePromptExecutionSettings()
    {
        var thinkingConfig = _configuration.GetSection("Thinking");
        var serviceConfig = thinkingConfig.GetSection($"Services:{_config.ThinkingService}");
        var providerType = serviceConfig.GetValue<string>("Provider")?.ToLower();

        return providerType switch
        {
            "ollama" => new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
            },
            "deepseek" or "openrouter" => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                ModelId = _config.ThinkingService
            },
            _ => throw new InvalidOperationException($"Unknown provider: {providerType}")
        };
    }
}

#pragma warning restore SKEXP0050
