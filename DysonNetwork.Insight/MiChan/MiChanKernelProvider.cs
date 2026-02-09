#pragma warning disable SKEXP0050
using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.MiChan;

public class MiChanKernelProvider
{
    private readonly MiChanConfig _config;
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MiChanKernelProvider> _logger;
    private Kernel? _kernel;
    private Kernel? _visionKernel;

    public MiChanKernelProvider(
        MiChanConfig config,
        KernelFactory kernelFactory,
        IConfiguration configuration,
        ILogger<MiChanKernelProvider> logger)
    {
        _config = config;
        _kernelFactory = kernelFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [Experimental("SKEXP0050")]
    public Kernel GetKernel()
    {
        if (_kernel != null)
            return _kernel;

        _kernel = _kernelFactory.CreateKernel(_config.ThinkingService, addEmbeddings: true);
        InitializeHelperFunctions(_kernel);
        return _kernel;
    }

    [Experimental("SKEXP0050")]
    public Kernel GetVisionKernel()
    {
        if (_visionKernel != null)
            return _visionKernel;

        _visionKernel = _kernelFactory.CreateKernel(_config.Vision.VisionThinkingService, addEmbeddings: false);
        return _visionKernel;
    }

    [Experimental("SKEXP0050")]
    private void InitializeHelperFunctions(Kernel kernel)
    {
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

    public PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null)
    {
        return _kernelFactory.CreatePromptExecutionSettings(_config.ThinkingService, temperature ?? 0.6);
    }

    public PromptExecutionSettings CreateVisionPromptExecutionSettings(double? temperature = null)
    {
        return _kernelFactory.CreatePromptExecutionSettings(_config.Vision.VisionThinkingService, temperature ?? 0.7);
    }

    public bool IsVisionModelAvailable()
    {
        return _kernelFactory.IsServiceAvailable(_config.Vision.VisionThinkingService);
    }
}

#pragma warning restore SKEXP0050
