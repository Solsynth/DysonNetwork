using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// A minimal, general-purpose kernel provider with embedding support.
/// Provides a single kernel instance configured from application settings.
/// </summary>
#pragma warning disable SKEXP0050
public class GeneralKernelProvider : IKernelProvider
{
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeneralKernelProvider> _logger;
    private readonly string _serviceId;

    private Kernel? _kernel;

    /// <summary>
    /// Creates a new instance of GeneralKernelProvider.
    /// </summary>
    /// <param name="kernelFactory">Factory for creating kernels</param>
    /// <param name="configuration">Configuration for service settings</param>
    /// <param name="logger">Logger instance</param>
    public GeneralKernelProvider(
        KernelFactory kernelFactory,
        IConfiguration configuration,
        ILogger<GeneralKernelProvider> logger)
    {
        _kernelFactory = kernelFactory;
        _configuration = configuration;
        _logger = logger;

        // Get the default service ID from configuration (same as ThoughtProvider)
        _serviceId = configuration.GetValue<string>("Thinking:DefaultService")!;

        if (string.IsNullOrEmpty(_serviceId))
        {
            throw new InvalidOperationException("Thinking:DefaultService is not configured.");
        }
    }

    /// <summary>
    /// Gets the kernel instance with embedding support enabled.
    /// Lazily initializes the kernel on first access.
    /// </summary>
    /// <returns>The configured Kernel instance with embeddings</returns>
    [Experimental("SKEXP0050")]
    public Kernel GetKernel()
    {
        if (_kernel != null)
            return _kernel;

        _kernel = _kernelFactory.CreateKernel(_serviceId, addEmbeddings: true);
        InitializeHelperFunctions(_kernel);

        _logger.LogInformation("GeneralKernelProvider initialized with service: {ServiceId}", _serviceId);

        return _kernel;
    }

    /// <summary>
    /// Creates prompt execution settings for the configured service.
    /// </summary>
    /// <param name="temperature">Optional temperature override (default: 0.7)</param>
    /// <returns>Configured PromptExecutionSettings</returns>
    [Experimental("SKEXP0050")]
    public PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null)
    {
        return _kernelFactory.CreatePromptExecutionSettings(_serviceId, temperature ?? 0.7);
    }

    /// <summary>
    /// Initializes helper functions (web search plugins) on the kernel.
    /// </summary>
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
            _logger.LogDebug("Bing web search plugin initialized");
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
            _logger.LogDebug("Google web search plugin initialized");
        }
    }
}

#pragma warning restore SKEXP0050
