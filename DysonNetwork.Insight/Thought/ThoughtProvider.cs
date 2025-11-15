using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.Thought.Plugins;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.Thought;

public class ThoughtProvider
{
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThoughtProvider> _logger;

    public Kernel Kernel { get; }

    private string? ModelProviderType { get; set; }
    public string? ModelDefault { get; set; }
    public List<string> ModelAvailable { get; set; } = [];

    [Experimental("SKEXP0050")]
    public ThoughtProvider(
        IConfiguration configuration,
        PostService.PostServiceClient postServiceClient,
        AccountService.AccountServiceClient accountServiceClient,
        ILogger<ThoughtProvider> logger
    )
    {
        _logger = logger;
        _postClient = postServiceClient;
        _accountClient = accountServiceClient;
        _configuration = configuration;

        Kernel = InitializeThinkingProvider(configuration);
        InitializeHelperFunctions();
    }

    private Kernel InitializeThinkingProvider(IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Thinking");
        ModelProviderType = cfg.GetValue<string>("Provider")?.ToLower();
        ModelDefault = cfg.GetValue<string>("Model");
        ModelAvailable = cfg.GetValue<List<string>>("ModelAvailable") ?? [];
        var endpoint = cfg.GetValue<string>("Endpoint");
        var apiKey = cfg.GetValue<string>("ApiKey");

        var builder = Kernel.CreateBuilder();

        switch (ModelProviderType)
        {
            case "ollama":
                foreach (var model in ModelAvailable)
                    builder.AddOllamaChatCompletion(
                        ModelDefault!,
                        new Uri(endpoint ?? "http://localhost:11434/api"),
                        model
                    );
                break;
            case "deepseek":
                var client = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                foreach (var model in ModelAvailable)
                    builder.AddOpenAIChatCompletion(ModelDefault!, client, model);
                break;
            default:
                throw new IndexOutOfRangeException("Unknown thinking provider: " + ModelProviderType);
        }

        // Add gRPC clients for Thought Plugins
        builder.Services.AddServiceDiscoveryCore();
        builder.Services.AddServiceDiscovery();
        builder.Services.AddAccountService();
        builder.Services.AddSphereService();

        builder.Plugins.AddFromObject(new SnAccountKernelPlugin(_accountClient));
        builder.Plugins.AddFromObject(new SnPostKernelPlugin(_postClient));

        return builder.Build();
    }

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions()
    {
        // Add web search plugins if configured
        var bingApiKey = _configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            Kernel.ImportPluginFromObject(bing, "bing");
        }

        var googleApiKey = _configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = _configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            Kernel.ImportPluginFromObject(google, "google");
        }
    }

    public PromptExecutionSettings CreatePromptExecutionSettings()
    {
        switch (ModelProviderType)
        {
            case "ollama":
                return new OllamaPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
                };
            case "deepseek":
                return new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
                };
            default:
                throw new InvalidOperationException("Unknown provider: " + ModelProviderType);
        }
    }
}