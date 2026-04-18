#pragma warning disable SKEXP0050, SKEXP0010

using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.MiChan;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Google;

namespace DysonNetwork.Insight.Agent.KernelBuilding;

/// <summary>
/// Fluent builder for constructing Semantic Kernel instances with consistent configuration.
/// This is the core implementation that doesn't depend on specific plugin types.
/// </summary>
public class SemanticKernelBuilder : IKernelBuilder
{
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _configuration;
    private KernelBuildOptions _options = new() { Model = new Models.ModelConfiguration() };

    public SemanticKernelBuilder(KernelFactory kernelFactory, IConfiguration configuration)
    {
        _kernelFactory = kernelFactory;
        _configuration = configuration;
    }

    public IKernelBuilder WithModel(Models.ModelConfiguration model)
    {
        _options.Model = model;
        return this;
    }

    public IKernelBuilder WithModel(string modelId)
    {
        _options.Model = new Models.ModelConfiguration { ModelId = modelId };
        return this;
    }

    public IKernelBuilder WithModel(Models.ModelRef modelRef)
    {
        _options.Model = modelRef;
        return this;
    }

    public IKernelBuilder WithEmbeddings(bool include = true)
    {
        _options.IncludeEmbeddings = include;
        return this;
    }

    public IKernelBuilder WithWebSearch(bool include = true)
    {
        _options.IncludeWebSearch = include;
        return this;
    }

    public IKernelBuilder WithPlugins(Action<Kernel> setup)
    {
        _options.PluginSetups.Add(setup);
        return this;
    }

    public IKernelBuilder WithTemperature(double temperature)
    {
        _options.Temperature = temperature;
        return this;
    }

    public IKernelBuilder WithReasoningEffort(string effort)
    {
        _options.ReasoningEffort = effort;
        return this;
    }

    public IKernelBuilder WithMaxTokens(int maxTokens)
    {
        _options.MaxTokens = maxTokens;
        return this;
    }

    public IKernelBuilder WithAutoInvoke(bool autoInvoke = true)
    {
        _options.AutoInvokeFunctions = autoInvoke;
        return this;
    }

    public IKernelBuilder WithServiceProvider(IServiceProvider serviceProvider)
    {
        _options.ServiceProvider = serviceProvider;
        return this;
    }

    public IKernelBuilder WithOptions(KernelBuildOptions options)
    {
        _options = options.Clone();
        return this;
    }

    [Experimental("SKEXP0050")]
    public Kernel Build()
    {
        // Clone options to avoid accumulating state across multiple builds
        // This is important because the builder is registered as a singleton
        var options = _options.Clone();
        
        Kernel kernel;

        // Check if using custom provider configuration (BaseUrl, ApiKey, etc.)
        var hasCustomConfig = !string.IsNullOrEmpty(options.Model.BaseUrl) ||
                              !string.IsNullOrEmpty(options.Model.ApiKey) ||
                              !string.IsNullOrEmpty(options.Model.Provider) ||
                              !string.IsNullOrEmpty(options.Model.CustomModelName);

        if (hasCustomConfig)
        {
            // Use ModelConfiguration-based creation for custom providers
            kernel = _kernelFactory.CreateKernel(options.Model, options.IncludeEmbeddings);
        }
        else
        {
            // Use service name-based creation for standard providers
            kernel = _kernelFactory.CreateKernel(
                options.Model.ModelId,
                options.IncludeEmbeddings);
        }

        // Add web search if requested
        if (options.IncludeWebSearch)
        {
            AddWebSearchPlugins(kernel);
        }

        // Apply custom plugin setups
        foreach (var setup in options.PluginSetups)
        {
            setup(kernel);
        }

        // Reset the builder state to prevent accumulation across builds
        _options = new KernelBuildOptions { Model = new Models.ModelConfiguration() };

        return kernel;
    }

    public PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        var temp = temperature ?? _options.Temperature ?? _options.Model.GetEffectiveTemperature();
        var effort = reasoningEffort ?? _options.ReasoningEffort ?? _options.Model.GetEffectiveReasoningEffort();

        // Use effective provider from ModelConfiguration (supports custom providers)
        var provider = _options.Model.GetEffectiveProvider().ToLower();
        var modelName = _options.Model.GetEffectiveModelName();

        return provider switch
        {
            "ollama" => new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = _options.Model.EnableFunctions
                    ? FunctionChoiceBehavior.Auto(autoInvoke: _options.AutoInvokeFunctions)
                    : null,
                Temperature = (float)temp
            },
            _ => new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = _options.Model.EnableFunctions
                    ? FunctionChoiceBehavior.Auto(autoInvoke: _options.AutoInvokeFunctions)
                    : null,
                ModelId = modelName,
                Temperature = (float)temp,
                ReasoningEffort = effort,
                MaxTokens = _options.MaxTokens ?? _options.Model.MaxTokens
            }
        };
    }

    public KernelBuildOptions GetOptions() => _options.Clone();

    [Experimental("SKEXP0050")]
    private void AddWebSearchPlugins(Kernel kernel)
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
}

#pragma warning restore SKEXP0050, SKEXP0010
