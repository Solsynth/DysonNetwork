using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.Agent.KernelBuilding;

/// <summary>
/// Options for building a kernel instance
/// </summary>
public class KernelBuildOptions
{
    /// <summary>
    /// The model configuration to use
    /// </summary>
    public required Models.ModelConfiguration Model { get; set; }

    /// <summary>
    /// Whether to add embedding services
    /// </summary>
    public bool IncludeEmbeddings { get; set; } = true;

    /// <summary>
    /// Whether to add web search plugins (Bing/Google)
    /// </summary>
    public bool IncludeWebSearch { get; set; } = false;

    /// <summary>
    /// Whether to auto-invoke function calls
    /// </summary>
    public bool AutoInvokeFunctions { get; set; } = false;

    /// <summary>
    /// Temperature override (if null, uses model configuration)
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Reasoning effort override (if null, uses model configuration)
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Max tokens override
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Service provider for resolving plugin dependencies
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Custom plugin setup actions
    /// </summary>
    public List<Action<Kernel>> PluginSetups { get; set; } = new();

    /// <summary>
    /// Creates a copy of these options
    /// </summary>
    public KernelBuildOptions Clone()
    {
        return new KernelBuildOptions
        {
            Model = Model.Clone(),
            IncludeEmbeddings = IncludeEmbeddings,
            IncludeWebSearch = IncludeWebSearch,
            AutoInvokeFunctions = AutoInvokeFunctions,
            Temperature = Temperature,
            ReasoningEffort = ReasoningEffort,
            MaxTokens = MaxTokens,
            ServiceProvider = ServiceProvider,
            PluginSetups = new List<Action<Kernel>>(PluginSetups)
        };
    }

    /// <summary>
    /// Creates default options for a model
    /// </summary>
    public static KernelBuildOptions ForModel(Models.ModelConfiguration model)
    {
        return new KernelBuildOptions { Model = model };
    }

    /// <summary>
    /// Creates default options for a model ID
    /// </summary>
    public static KernelBuildOptions ForModel(string modelId)
    {
        return new KernelBuildOptions { Model = new Models.ModelConfiguration { ModelId = modelId } };
    }

    /// <summary>
    /// Creates default options for a ModelRef
    /// </summary>
    public static KernelBuildOptions ForModel(Models.ModelRef modelRef)
    {
        return new KernelBuildOptions { Model = modelRef };
    }
}
