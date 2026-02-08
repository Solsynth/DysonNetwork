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
                var client = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, client);
                // Note: DeepSeek doesn't provide embeddings yet, we'll need OpenAI or Azure for embeddings
                // Fall back to OpenAI embeddings if available
                AddOpenAIEmbeddingsIfAvailable(builder);
                break;
            default:
                throw new InvalidOperationException($"Unknown thinking provider: {providerType}");
        }

        // Add web search plugins if configured
        InitializeHelperFunctions(builder);

        _kernel = builder.Build();
        return _kernel;
    }

    private void AddOpenAIEmbeddingsIfAvailable(IKernelBuilder builder)
    {
        var openAiKey = _configuration.GetValue<string>("Thinking:OpenAI:ApiKey");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            builder.AddOpenAITextEmbeddingGeneration(
                "text-embedding-3-small",  // Cheapest and good quality
                openAiKey
            );
            _logger.LogInformation("OpenAI embeddings configured");
        }
        else
        {
            _logger.LogWarning("No embedding service configured. Semantic search will be disabled.");
        }
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
            "deepseek" => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                ModelId = _config.ThinkingService
            },
            _ => throw new InvalidOperationException($"Unknown provider: {providerType}")
        };
    }
}

#pragma warning restore SKEXP0050
