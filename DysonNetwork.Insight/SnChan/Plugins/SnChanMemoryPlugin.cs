using System.Text;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Thought.Memory;

namespace DysonNetwork.Insight.SnChan.Plugins;

public class SnChanMemoryPlugin(
    MemoryService memoryService,
    ILogger<SnChanMemoryPlugin> logger)
{
    private const string BotName = "snchan";

    [AgentTool("search_memory", Description = "Search SnChan memory database for relevant information to improve consistency and personalization.")]
    public async Task<string> SearchMemoryAsync(
        [AgentToolParameter("What to search for.")] string query,
        [AgentToolParameter("Optional memory type filter.")] string? memoryType = null,
        [AgentToolParameter("Optional account id for user-scoped memories.")] Guid? accountId = null,
        [AgentToolParameter("Maximum results to return.")] int limit = 10)
    {
        try
        {
            var memories = await memoryService.SearchAsync(
                query: query,
                type: memoryType,
                accountId: accountId,
                limit: limit,
                minSimilarity: 0.6,
                botName: BotName);

            if (memories.Count == 0)
            {
                return "No relevant memories found for your query.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Found {memories.Count} relevant memories:");
            results.AppendLine();

            for (var i = 0; i < memories.Count; i++)
            {
                results.AppendLine($"--- Memory {i + 1} ---");
                results.AppendLine(memories[i].ToPrompt());
                results.AppendLine();
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching SnChan memory for query: {Query}", query);
            return $"Error searching memory: {ex.Message}";
        }
    }

    [AgentTool("store_memory", Description = "Store a new SnChan memory entry.")]
    public async Task<string> StoreMemoryAsync(
        [AgentToolParameter("Memory type.")] string type,
        [AgentToolParameter("Memory content.")] string content,
        [AgentToolParameter("Optional confidence 0..1.")] float? confidence = null,
        [AgentToolParameter("Optional account id for user-scoped memory.")] Guid? accountId = null,
        [AgentToolParameter("Optional hot flag.")] bool? isHot = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                return "Error: Content cannot be empty.";

            if (string.IsNullOrWhiteSpace(type))
                return "Error: Type cannot be empty.";

            type = type.ToLowerInvariant();
            if (type == "user" && accountId is null)
                return "Error: user memory requires accountId.";

            var record = await memoryService.StoreMemoryAsync(
                type: type,
                content: content,
                confidence: confidence ?? 0.7f,
                accountId: accountId,
                hot: isHot ?? false,
                botName: BotName);

            return $"Memory stored successfully with ID: {record.Id}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing SnChan memory");
            return $"Error storing memory: {ex.Message}";
        }
    }
}
