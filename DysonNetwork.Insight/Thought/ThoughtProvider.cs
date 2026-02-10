using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Thought.Plugins;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.SemanticKernel;
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
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ThoughtProvider> _logger;

    private readonly Dictionary<string, Kernel> _kernels = new();
    private readonly Dictionary<string, string> _serviceProviders = new();
    private readonly Dictionary<string, ThoughtServiceModel> _serviceModels = new();
    private readonly string _defaultServiceId;

    [Experimental("SKEXP0050")]
    public ThoughtProvider(
        KernelFactory kernelFactory,
        IConfiguration configuration,
        PostService.PostServiceClient postServiceClient,
        AccountService.AccountServiceClient accountServiceClient,
        ILogger<ThoughtProvider> logger
    )
    {
        _logger = logger;
        _kernelFactory = kernelFactory;
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

            var kernel = InitializeThinkingService(serviceId);
            _kernels[serviceId] = kernel;
            _serviceProviders[serviceId] = providerType;
        }
    }

    [Experimental("SKEXP0050")]
    private Kernel InitializeThinkingService(string serviceId)
    {
        // Create base kernel using factory (no embeddings needed for thought provider)
        var kernel = _kernelFactory.CreateKernel(serviceId, addEmbeddings: false);

        // Add Thought-specific plugins (gRPC clients are already injected)
        kernel.Plugins.AddFromObject(new SnAccountKernelPlugin(_accountClient));
        kernel.Plugins.AddFromObject(new SnPostKernelPlugin(_postClient));

        // Add helper functions (web search)
        InitializeHelperFunctions(kernel);

        return kernel;
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

    #pragma warning disable SKEXP0050
    public PromptExecutionSettings CreatePromptExecutionSettings(string? serviceId = null)
    {
        serviceId ??= _defaultServiceId;
        return _kernelFactory.CreatePromptExecutionSettings(serviceId);
    }
    #pragma warning restore SKEXP0050
}
