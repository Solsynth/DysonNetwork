using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Centralized plugin registration for MiChan kernels.
/// Provides a single place to register all MiChan plugins to avoid code duplication.
/// </summary>
#pragma warning disable SKEXP0050
public static class MiChanPluginExtensions
{
    /// <summary>
    /// Adds all MiChan plugins to the kernel if they are not already registered.
    /// This is the central method for registering MiChan plugins across the codebase.
    /// </summary>
    /// <param name="kernel">The kernel to add plugins to</param>
    /// <param name="serviceProvider">The service provider to resolve plugin instances</param>
    public static void AddMiChanPlugins(this Kernel kernel, IServiceProvider serviceProvider)
    {
        // Post plugin
        if (!kernel.Plugins.Contains("post"))
        {
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            kernel.Plugins.AddFromObject(postPlugin, "post");
        }

        // Account plugin
        if (!kernel.Plugins.Contains("account"))
        {
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();
            kernel.Plugins.AddFromObject(accountPlugin, "account");
        }

        // Memory plugin
        if (!kernel.Plugins.Contains("memory"))
        {
            var memoryPlugin = serviceProvider.GetRequiredService<MemoryPlugin>();
            kernel.Plugins.AddFromObject(memoryPlugin, "memory");
        }

        // Scheduled task plugin
        if (!kernel.Plugins.Contains("scheduledTasks"))
        {
            var scheduledTaskPlugin = serviceProvider.GetRequiredService<ScheduledTaskPlugin>();
            kernel.Plugins.AddFromObject(scheduledTaskPlugin, "scheduledTasks");
        }

        // Conversation plugin
        if (!kernel.Plugins.Contains("conversation"))
        {
            var conversationPlugin = serviceProvider.GetRequiredService<ConversationPlugin>();
            kernel.Plugins.AddFromObject(conversationPlugin, "conversation");
        }
    }

    /// <summary>
    /// Adds MiChan plugins to the kernel with optional filtering.
    /// Use this overload when you want to exclude certain plugins.
    /// </summary>
    /// <param name="kernel">The kernel to add plugins to</param>
    /// <param name="serviceProvider">The service provider to resolve plugin instances</param>
    /// <param name="excludePlugins">List of plugin names to exclude (e.g., "conversation", "scheduledTasks")</param>
    public static void AddMiChanPlugins(
        this Kernel kernel,
        IServiceProvider serviceProvider,
        params string[] excludePlugins)
    {
        var excludedSet = new HashSet<string>(excludePlugins, StringComparer.OrdinalIgnoreCase);

        // Post plugin
        if (!excludedSet.Contains("post") && !kernel.Plugins.Contains("post"))
        {
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            kernel.Plugins.AddFromObject(postPlugin, "post");
        }

        // Account plugin
        if (!excludedSet.Contains("account") && !kernel.Plugins.Contains("account"))
        {
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();
            kernel.Plugins.AddFromObject(accountPlugin, "account");
        }

        // Memory plugin
        if (!excludedSet.Contains("memory") && !kernel.Plugins.Contains("memory"))
        {
            var memoryPlugin = serviceProvider.GetRequiredService<MemoryPlugin>();
            kernel.Plugins.AddFromObject(memoryPlugin, "memory");
        }

        // Scheduled task plugin
        if (!excludedSet.Contains("scheduledTasks") && !kernel.Plugins.Contains("scheduledTasks"))
        {
            var scheduledTaskPlugin = serviceProvider.GetRequiredService<ScheduledTaskPlugin>();
            kernel.Plugins.AddFromObject(scheduledTaskPlugin, "scheduledTasks");
        }

        // Conversation plugin
        if (!excludedSet.Contains("conversation") && !kernel.Plugins.Contains("conversation"))
        {
            var conversationPlugin = serviceProvider.GetRequiredService<ConversationPlugin>();
            kernel.Plugins.AddFromObject(conversationPlugin, "conversation");
        }
    }

    /// <summary>
    /// Checks if all MiChan plugins are registered in the kernel.
    /// </summary>
    public static bool HasAllMiChanPlugins(this Kernel kernel)
    {
        return kernel.Plugins.Contains("post") &&
               kernel.Plugins.Contains("account") &&
               kernel.Plugins.Contains("memory") &&
               kernel.Plugins.Contains("scheduledTasks") &&
               kernel.Plugins.Contains("conversation");
    }

    /// <summary>
    /// Gets a list of missing MiChan plugin names.
    /// </summary>
    public static List<string> GetMissingMiChanPlugins(this Kernel kernel)
    {
        var missing = new List<string>();

        if (!kernel.Plugins.Contains("post"))
            missing.Add("post");
        if (!kernel.Plugins.Contains("account"))
            missing.Add("account");
        if (!kernel.Plugins.Contains("memory"))
            missing.Add("memory");
        if (!kernel.Plugins.Contains("scheduledTasks"))
            missing.Add("scheduledTasks");
        if (!kernel.Plugins.Contains("conversation"))
            missing.Add("conversation");

        return missing;
    }
}
#pragma warning restore SKEXP0050
