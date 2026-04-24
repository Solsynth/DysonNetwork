using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.SnChan.Plugins;

public static class SnChanPluginExtensions
{
    public static void RegisterSnChanPlugins(
        this IAgentToolRegistry registry,
        IServiceProvider serviceProvider,
        params string[] excludePlugins)
    {
        var excludedSet = new HashSet<string>(excludePlugins, StringComparer.OrdinalIgnoreCase);

        if (!excludedSet.Contains("post"))
        {
            var postPlugin = serviceProvider.GetRequiredService<SnChanPostPlugin>();
            registry.RegisterPluginTools(postPlugin, "post");
        }

        if (!excludedSet.Contains("memory"))
        {
            var memoryPlugin = serviceProvider.GetRequiredService<SnChanMemoryPlugin>();
            registry.RegisterPluginTools(memoryPlugin, "memory");
        }

        if (!excludedSet.Contains("mood"))
        {
            var moodPlugin = serviceProvider.GetRequiredService<SnChanMoodPlugin>();
            registry.RegisterPluginTools(moodPlugin, "mood");
        }

        if (!excludedSet.Contains("userProfile"))
        {
            var userProfilePlugin = serviceProvider.GetRequiredService<SnChanUserProfilePlugin>();
            registry.RegisterPluginTools(userProfilePlugin, "userProfile");
        }

        if (!excludedSet.Contains("swagger"))
        {
            var swaggerPlugin = serviceProvider.GetRequiredService<SnChanSwaggerPlugin>();
            registry.RegisterPluginTools(swaggerPlugin, "swagger");
        }
    }
}
