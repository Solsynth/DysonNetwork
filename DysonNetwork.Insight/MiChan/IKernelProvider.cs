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
    /// <returns>Configured PromptExecutionSettings</returns>
    [Experimental("SKEXP0050")]
    PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null);
}
