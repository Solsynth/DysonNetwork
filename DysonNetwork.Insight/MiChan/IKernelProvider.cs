using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Interface for kernel providers that supply Semantic Kernel instances
/// with optional embedding support.
/// </summary>
public interface IKernelProvider
{
    /// <summary>
    /// Gets the primary kernel instance with embedding support enabled.
    /// </summary>
    /// <returns>The configured Kernel instance</returns>
    Kernel GetKernel();

    /// <summary>
    /// Creates prompt execution settings for the kernel.
    /// </summary>
    /// <param name="temperature">Optional temperature override</param>
    /// <param name="reasoningEffort">Optional reasoning effort (low/medium/high)</param>
    /// <returns>Configured PromptExecutionSettings</returns>
    [Experimental("SKEXP0050")]
    PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null, string? reasoningEffort = null);

    /// <summary>
    /// Gets the kernel for autonomous behavior (may use different model).
    /// </summary>
    [Experimental("SKEXP0050")]
    Kernel GetAutonomousKernel();

    /// <summary>
    /// Creates prompt execution settings for autonomous behavior.
    /// </summary>
    [Experimental("SKEXP0050")]
    PromptExecutionSettings CreateAutonomousPromptExecutionSettings(double? temperature = null, string? reasoningEffort = null);
}
