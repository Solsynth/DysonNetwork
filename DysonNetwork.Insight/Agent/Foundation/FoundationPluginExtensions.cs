namespace DysonNetwork.Insight.Agent.Foundation;

using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.SnDoc;

public static class FoundationPluginExtensions
{
    public static void RegisterMiChanPlugins(
        this IAgentToolRegistry registry,
        IServiceProvider serviceProvider,
        params string[] excludePlugins)
    {
        var excludedSet = new HashSet<string>(excludePlugins, StringComparer.OrdinalIgnoreCase);

        if (!excludedSet.Contains("post"))
        {
            var postPlugin = serviceProvider.GetRequiredService<PostPlugin>();
            registry.RegisterPluginTools(postPlugin, "post");
        }

        if (!excludedSet.Contains("account"))
        {
            var accountPlugin = serviceProvider.GetRequiredService<AccountPlugin>();
            registry.RegisterPluginTools(accountPlugin, "account");
        }

        if (!excludedSet.Contains("webSearch"))
        {
            var webSearchPlugin = serviceProvider.GetRequiredService<WebSearchPlugin>();
            registry.RegisterPluginTools(webSearchPlugin, "webSearch");
        }

        if (!excludedSet.Contains("memory"))
        {
            var memoryPlugin = serviceProvider.GetRequiredService<MemoryPlugin>();
            registry.RegisterPluginTools(memoryPlugin, "memory");
        }

        if (!excludedSet.Contains("sequenceMemory"))
        {
            var sequenceMemoryPlugin = serviceProvider.GetRequiredService<SequenceMemoryPlugin>();
            registry.RegisterPluginTools(sequenceMemoryPlugin, "sequenceMemory");
        }

        if (!excludedSet.Contains("userProfile"))
        {
            var userProfilePlugin = serviceProvider.GetRequiredService<UserProfilePlugin>();
            registry.RegisterPluginTools(userProfilePlugin, "userProfile");
        }

        if (!excludedSet.Contains("scheduledTasks"))
        {
            var scheduledTaskPlugin = serviceProvider.GetRequiredService<ScheduledTaskPlugin>();
            registry.RegisterPluginTools(scheduledTaskPlugin, "scheduledTasks");
        }

        if (!excludedSet.Contains("conversation"))
        {
            var conversationPlugin = serviceProvider.GetRequiredService<ConversationPlugin>();
            registry.RegisterPluginTools(conversationPlugin, "conversation");
        }

        if (!excludedSet.Contains("mood"))
        {
            var moodPlugin = serviceProvider.GetRequiredService<MoodPlugin>();
            registry.RegisterPluginTools(moodPlugin, "mood");
        }

        if (!excludedSet.Contains("fitness"))
        {
            var fitnessPlugin = serviceProvider.GetRequiredService<FitnessPlugin>();
            registry.RegisterPluginTools(fitnessPlugin, "fitness");
        }

        if (!excludedSet.Contains("docs"))
        {
            var snDocPlugin = serviceProvider.GetRequiredService<SnDocPlugin>();
            registry.RegisterPluginTools(snDocPlugin, "docs");
        }
    }

    public static List<string> GetMiChanPluginToolNames()
    {
        return
        [
            "post.get_post",
            "post.create_post",
            "post.react_to_post",
            "post.get_timeline",
            "account.get_account",
            "account.search_accounts",
            "account.get_current_user",
            "account.update_user_nickname",
            "webSearch.web_search",
            "webSearch.search_web",
            "memory.search_memory",
            "memory.store_memory",
            "sequenceMemory.search_sequence_memory",
            "sequenceMemory.store_sequence_memory",
            "userProfile.get_user_profile",
            "scheduledTasks.create_scheduled_task",
            "scheduledTasks.list_scheduled_tasks",
            "scheduledTasks.delete_scheduled_task",
            "conversation.summarize_conversation",
            "mood.get_mood",
            "mood.update_mood",
            "fitness.get_fitness_data",
            "fitness.log_fitness",
            "docs.search_docs",
            "docs.read_doc",
            "docs.list_docs",
            "docs.get_doc_by_slug"
        ];
    }
}
