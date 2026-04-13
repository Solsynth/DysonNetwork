#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.MiChan;

public class MiChanKernelProvider(
    MiChanConfig config,
    KernelFactory kernelFactory,
    IConfiguration configuration
) : IKernelProvider
{
    private string GetAutonomousServiceName()
        => string.IsNullOrEmpty(config.AutonomousThinkingService) 
            ? config.ThinkingService 
            : config.AutonomousThinkingService;

    [Experimental("SKEXP0050")]
    public Kernel GetKernel()
    {
        var kernel = kernelFactory.CreateKernel(config.ThinkingService, addEmbeddings: true);
        InitializeHelperFunctions(kernel);
        return kernel;
    }

    [Experimental("SKEXP0050")]
    public Kernel GetAutonomousKernel()
    {
        var serviceName = GetAutonomousServiceName();
        var kernel = kernelFactory.CreateKernel(serviceName, addEmbeddings: true);
        InitializeHelperFunctions(kernel);
        return kernel;
    }

    [Experimental("SKEXP0050")]
    public Kernel GetVisionKernel()
    {
        return kernelFactory.CreateKernel(config.Vision.VisionThinkingService, addEmbeddings: false);
    }

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions(Kernel kernel)
    {
        var bingApiKey = configuration.GetValue<string>("Thinking:BingApiKey");
        if (!string.IsNullOrEmpty(bingApiKey))
        {
            var bingConnector = new BingConnector(bingApiKey);
            var bing = new WebSearchEnginePlugin(bingConnector);
            kernel.ImportPluginFromObject(bing, "bing");
        }

        var googleApiKey = configuration.GetValue<string>("Thinking:GoogleApiKey");
        var googleCx = configuration.GetValue<string>("Thinking:GoogleCx");
        if (!string.IsNullOrEmpty(googleApiKey) && !string.IsNullOrEmpty(googleCx))
        {
            var googleConnector = new GoogleConnector(
                apiKey: googleApiKey,
                searchEngineId: googleCx);
            var google = new WebSearchEnginePlugin(googleConnector);
            kernel.ImportPluginFromObject(google, "google");
        }
    }

    public PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        return kernelFactory.CreatePromptExecutionSettings(config.ThinkingService, temperature ?? 0.75, reasoningEffort);
    }

    public PromptExecutionSettings CreateAutonomousPromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        var serviceName = GetAutonomousServiceName();
        var defaultEffort = serviceName != config.ThinkingService ? "high" : null;
        return kernelFactory.CreatePromptExecutionSettings(serviceName, temperature ?? 0.7, reasoningEffort ?? defaultEffort);
    }

    public PromptExecutionSettings CreateVisionPromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        return kernelFactory.CreatePromptExecutionSettings(config.Vision.VisionThinkingService, temperature ?? 0.7, reasoningEffort);
    }

    public PromptExecutionSettings CreateScheduledTaskPromptExecutionSettings(double? temperature = null)
    {
        return kernelFactory.CreateScheduledTaskPromptExecutionSettings(config.ThinkingService, temperature ?? 0.7);
    }

    public bool IsVisionModelAvailable()
    {
        return kernelFactory.IsServiceAvailable(config.Vision.VisionThinkingService);
    }

    public string GetGatewayUrl()
    {
        return config.GatewayUrl;
    }

    public string GetAccessToken()
    {
        return config.AccessToken;
    }

    public string GetServiceId()
    {
        return config.ThinkingService;
    }
}

#pragma warning restore SKEXP0050
