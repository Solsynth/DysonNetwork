using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.Agent.KernelBuilding;

/// <summary>
/// Fluent builder interface for constructing Semantic Kernel instances
/// </summary>
public interface IKernelBuilder
{
    /// <summary>
    /// Sets the model configuration to use
    /// </summary>
    IKernelBuilder WithModel(Models.ModelConfiguration model);

    /// <summary>
    /// Sets the model by ID
    /// </summary>
    IKernelBuilder WithModel(string modelId);

    /// <summary>
    /// Sets the model by ModelRef
    /// </summary>
    IKernelBuilder WithModel(Models.ModelRef modelRef);

    /// <summary>
    /// Adds embedding services to the kernel
    /// </summary>
    IKernelBuilder WithEmbeddings(bool include = true);

    /// <summary>
    /// Adds web search plugins (Bing/Google based on configuration)
    /// </summary>
    IKernelBuilder WithWebSearch(bool include = true);

    /// <summary>
    /// Adds a custom plugin setup action
    /// </summary>
    IKernelBuilder WithPlugins(Action<Kernel> setup);

    /// <summary>
    /// Sets the temperature for the model
    /// </summary>
    IKernelBuilder WithTemperature(double temperature);

    /// <summary>
    /// Sets the reasoning effort (low/medium/high)
    /// </summary>
    IKernelBuilder WithReasoningEffort(string effort);

    /// <summary>
    /// Sets the max tokens
    /// </summary>
    IKernelBuilder WithMaxTokens(int maxTokens);

    /// <summary>
    /// Enables or disables auto-invocation of functions
    /// </summary>
    IKernelBuilder WithAutoInvoke(bool autoInvoke = true);

    /// <summary>
    /// Sets the service provider for dependency injection
    /// </summary>
    IKernelBuilder WithServiceProvider(IServiceProvider serviceProvider);

    /// <summary>
    /// Applies configuration from KernelBuildOptions
    /// </summary>
    IKernelBuilder WithOptions(KernelBuildOptions options);

    /// <summary>
    /// Builds the kernel instance
    /// </summary>
    Kernel Build();

    /// <summary>
    /// Creates prompt execution settings based on current configuration
    /// </summary>
    PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null, string? reasoningEffort = null);

    /// <summary>
    /// Gets the current build options
    /// </summary>
    KernelBuildOptions GetOptions();
}
