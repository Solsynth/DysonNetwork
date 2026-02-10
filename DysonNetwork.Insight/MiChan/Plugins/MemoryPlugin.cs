using System.ComponentModel;
using Microsoft.SemanticKernel;
using System.Text;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for searching and retrieving memories from MiChan's memory database
/// </summary>
public class MemoryPlugin(
    MiChanMemoryService memoryService,
    ILogger<MemoryPlugin> logger)
{
    /// <summary>
    /// Search through MiChan's memory for information related to a query
    /// </summary>
    [KernelFunction("search_memory")]
    [Description("Search MiChan's memory database for past interactions, conversations, or information related to a query. Use this when you need to recall previous conversations or find relevant context.")]
    public async Task<string> SearchMemoryAsync(
        [Description("The search query to find relevant memories")] string query,
        [Description("Optional: Type of memory to search (e.g., 'chat', 'thought', 'thought_sequence', 'autonomous'). Leave empty to search all types.")] string? memoryType = null,
        [Description("Maximum number of results to return (default: 10)")] int limit = 10)
    {
        try
        {
            logger.LogInformation("Searching memory for query: {Query}", query);

            var memories = await memoryService.SearchSimilarInteractionsAsync(
                query,
                limit: limit,
                minSimilarity: 0.6);

            if (memories.Count == 0)
            {
                return "No relevant memories found for your query.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Found {memories.Count} relevant memories:");
            results.AppendLine();

            for (int i = 0; i < memories.Count; i++)
            {
                var memory = memories[i];
                results.AppendLine($"--- Memory {i + 1} ---");
                results.AppendLine($"Type: {memory.Type}");
                results.AppendLine($"Context ID: {memory.ContextId}");
                results.AppendLine($"Created: {memory.CreatedAt}");

                // Try to extract meaningful content based on memory type
                if (memory.Context.TryGetValue("content", out var content))
                {
                    results.AppendLine($"Content: {content}");
                }
                else if (memory.Context.TryGetValue("message", out var message))
                {
                    results.AppendLine($"Message: {message}");
                    if (memory.Context.TryGetValue("response", out var response))
                    {
                        results.AppendLine($"Response: {response}");
                    }
                }
                else if (memory.Context.TryGetValue("userMessage", out var userMessage))
                {
                    results.AppendLine($"User: {userMessage}");
                    if (memory.Context.TryGetValue("aiResponse", out var aiResponse))
                    {
                        results.AppendLine($"Assistant: {aiResponse}");
                    }
                }

                // Add embedded content if available
                if (!string.IsNullOrEmpty(memory.EmbeddedContent))
                {
                    results.AppendLine($"Summary: {memory.EmbeddedContent}");
                }

                results.AppendLine();
            }

            logger.LogInformation("Memory search completed, found {Count} results", memories.Count);
            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching memory for query: {Query}", query);
            return $"Error searching memory: {ex.Message}";
        }
    }

    /// <summary>
    /// Get recent memories for a specific context
    /// </summary>
    [KernelFunction("get_recent_memories")]
    [Description("Retrieve recent memories for a specific context (like a chat room or conversation). Use this to get the most recent interactions.")]
    public async Task<string> GetRecentMemoriesAsync(
        [Description("The context ID (e.g., chat room ID, sequence ID)")] string contextId,
        [Description("Number of recent memories to retrieve (default: 10)")] int count = 10)
    {
        try
        {
            logger.LogInformation("Getting recent memories for context: {ContextId}", contextId);

            var memories = await memoryService.GetRecentInteractionsAsync(contextId, count);

            if (memories.Count == 0)
            {
                return $"No recent memories found for context '{contextId}'.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Recent memories for context '{contextId}':");
            results.AppendLine();

            foreach (var memory in memories.OrderBy(m => m.CreatedAt))
            {
                results.AppendLine($"[{memory.CreatedAt}] Type: {memory.Type}");

                if (memory.Context.TryGetValue("message", out var message))
                {
                    results.AppendLine($"  Message: {message}");
                }
                if (memory.Context.TryGetValue("response", out var response))
                {
                    results.AppendLine($"  Response: {response}");
                }
                if (memory.Context.TryGetValue("content", out var content))
                {
                    results.AppendLine($"  Content: {content}");
                }

                results.AppendLine();
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting recent memories for context: {ContextId}", contextId);
            return $"Error retrieving memories: {ex.Message}";
        }
    }

    /// <summary>
    /// Get memories by specific type
    /// </summary>
    [KernelFunction("get_memories_by_type")]
    [Description("Retrieve memories filtered by type. Use this to find specific kinds of memories like 'thought' for AI thoughts, 'chat' for chat conversations, or 'autonomous' for autonomous actions.")]
    public async Task<string> GetMemoriesByTypeAsync(
        [Description("The type of memory to retrieve (e.g., 'chat', 'thought', 'thought_sequence', 'autonomous')")] string memoryType,
        [Description("Optional: A semantic query to filter results further")] string? semanticQuery = null,
        [Description("Maximum number of results (default: 10)")] int limit = 10)
    {
        try
        {
            logger.LogInformation("Getting memories of type: {MemoryType}", memoryType);

            var memories = await memoryService.GetInteractionsByTypeAsync(
                memoryType,
                semanticQuery,
                limit);

            if (memories.Count == 0)
            {
                return $"No memories found of type '{memoryType}'.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Memories of type '{memoryType}':");
            results.AppendLine();

            for (int i = 0; i < memories.Count; i++)
            {
                var memory = memories[i];
                results.AppendLine($"--- Memory {i + 1} ---");
                results.AppendLine($"Context ID: {memory.ContextId}");
                results.AppendLine($"Created: {memory.CreatedAt}");

                // Try to extract content
                foreach (var kvp in memory.Context.Where(k => k.Key != "timestamp" && k.Value != null))
                {
                    results.AppendLine($"{kvp.Key}: {kvp.Value}");
                }

                results.AppendLine();
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting memories by type: {MemoryType}", memoryType);
            return $"Error retrieving memories: {ex.Message}";
        }
    }

    /// <summary>
    /// Check if there are any memories about a specific topic or entity
    /// </summary>
    [KernelFunction("has_memories_about")]
    [Description("Check if MiChan has any memories about a specific topic, person, or entity. Returns true if relevant memories exist, false otherwise.")]
    public async Task<bool> HasMemoriesAboutAsync(
        [Description("The topic, person, or entity to check for")] string topic)
    {
        try
        {
            logger.LogInformation("Checking for memories about: {Topic}", topic);

            var memories = await memoryService.SearchSimilarInteractionsAsync(
                topic,
                limit: 1,
                minSimilarity: 0.7);

            return memories.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking memories about: {Topic}", topic);
            return false;
        }
    }

    /// <summary>
    /// Get a summary of what MiChan remembers about a specific topic
    /// </summary>
    [KernelFunction("summarize_memories")]
    [Description("Get a summary of what MiChan remembers about a specific topic or query. This provides a condensed view of relevant memories.")]
    public async Task<string> SummarizeMemoriesAsync(
        [Description("The topic or query to summarize memories about")] string topic,
        [Description("Maximum number of memories to include (default: 5)")] int maxMemories = 5)
    {
        try
        {
            logger.LogInformation("Summarizing memories about: {Topic}", topic);

            var memories = await memoryService.SearchSimilarInteractionsAsync(
                topic,
                limit: maxMemories,
                minSimilarity: 0.6);

            if (memories.Count == 0)
            {
                return $"I don't have any specific memories about '{topic}'.";
            }

            var summary = new StringBuilder();
            summary.AppendLine($"Based on my memories, here's what I know about '{topic}':");
            summary.AppendLine();

            // Group memories by type for better organization
            var groupedMemories = memories.GroupBy(m => m.Type).OrderBy(g => g.Key);

            foreach (var group in groupedMemories)
            {
                summary.AppendLine($"From {group.Key} interactions:");

                foreach (var memory in group.Take(3))
                {
                    if (memory.Context.TryGetValue("content", out var content))
                    {
                        summary.AppendLine($"  - {content}");
                    }
                    else if (memory.Context.TryGetValue("message", out var message))
                    {
                        var msg = message?.ToString() ?? "";
                        if (msg.Length > 100) msg = msg[..100] + "...";
                        summary.AppendLine($"  - {msg}");
                    }
                }

                summary.AppendLine();
            }

            return summary.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error summarizing memories about: {Topic}", topic);
            return $"Error retrieving memory summary: {ex.Message}";
        }
    }
}
