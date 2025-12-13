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

public class ThoughtServiceModel
{
    public string ServiceId { get; set; } = null!;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public double BillingMultiplier { get; set; }
    public int PerkLevel { get; set; }
}

public class ThoughtProvider
{
    private readonly PostService.PostServiceClient _postClient;
    private readonly AccountService.AccountServiceClient _accountClient;
    private readonly IConfiguration _configuration;

    private readonly Dictionary<string, Kernel> _kernels = new();
    private readonly Dictionary<string, string> _serviceProviders = new();
    private readonly Dictionary<string, ThoughtServiceModel> _serviceModels = new();
    private readonly string _defaultServiceId;

    [Experimental("SKEXP0050")]
    public ThoughtProvider(
        IConfiguration configuration,
        PostService.PostServiceClient postServiceClient,
        AccountService.AccountServiceClient accountServiceClient
    )
    {
        _postClient = postServiceClient;
        _accountClient = accountServiceClient;
        _configuration = configuration;

        var cfg = configuration.GetSection("Thinking");
        _defaultServiceId = cfg.GetValue<string>("DefaultService")!;
        var services = cfg.GetSection("Services").GetChildren();

        foreach (var service in services)
        {
            var serviceId = service.Key;
            var serviceModel = new ThoughtServiceModel
            {
                ServiceId = serviceId,
                Provider = service.GetValue<string>("Provider"),
                Model = service.GetValue<string>("Model"),
                BillingMultiplier = service.GetValue<double>("BillingMultiplier", 1.0),
                PerkLevel = service.GetValue<int>("PerkLevel", 0)
            };
            _serviceModels[serviceId] = serviceModel;
            
            var providerType = service.GetValue<string>("Provider")?.ToLower();
            if (providerType is null) continue;

            var kernel = InitializeThinkingService(service);
            InitializeHelperFunctions(kernel);
            _kernels[serviceId] = kernel;
            _serviceProviders[serviceId] = providerType;
        }
    }

    private Kernel InitializeThinkingService(IConfigurationSection serviceConfig)
    {
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
                break;
            case "deepseek":
                var client = new OpenAIClient(
                    new ApiKeyCredential(apiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? "https://api.deepseek.com/v1") }
                );
                builder.AddOpenAIChatCompletion(model!, client);
                break;
            default:
                throw new IndexOutOfRangeException("Unknown thinking provider: " + providerType);
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
    private void InitializeHelperFunctions(Kernel kernel)
    {
        // Add web search plugins if configured
        var bingApiKey = _configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            kernel.ImportPluginFromObject(bing, "bing");
        }

        var googleApiKey = _configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = _configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            kernel.ImportPluginFromObject(google, "google");
        }
    }

    public Kernel? GetKernel(string? serviceId = null)
    {
        serviceId ??= _defaultServiceId;
        return _kernels.GetValueOrDefault(serviceId);
    }

    public string GetServiceId(string? serviceId = null)
    {
        return serviceId ?? _defaultServiceId;
    }

    public IEnumerable<string> GetAvailableServices()
    {
        return _kernels.Keys;
    }
    
    public IEnumerable<ThoughtServiceModel> GetAvailableServicesInfo()
    {
        return _serviceModels.Values;
    }

    public ThoughtServiceModel? GetServiceInfo(string? serviceId)
    {
        serviceId ??= _defaultServiceId;
        return _serviceModels.GetValueOrDefault(serviceId);
    }

    public string GetDefaultServiceId()
    {
        return _defaultServiceId;
    }

    public PromptExecutionSettings CreatePromptExecutionSettings(string? serviceId = null)
    {
        serviceId ??= _defaultServiceId;
        var providerType = _serviceProviders.GetValueOrDefault(serviceId);

        return providerType switch
        {
            "ollama" => new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
            },
            "deepseek" => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false), ModelId = serviceId
            },
            _ => throw new InvalidOperationException("Unknown provider for service: " + serviceId)
        };
    }
}