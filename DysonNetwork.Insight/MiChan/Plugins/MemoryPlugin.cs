using System.ComponentModel;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;
using System.Text;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for searching and retrieving memories from MiChan's memory database
/// </summary>
public class MemoryPlugin(
    MemoryService memoryService,
    ILogger<MemoryPlugin> logger)
{
    /// <summary>
    /// Search through MiChan's memory for information related to a query
    /// </summary>
    [KernelFunction("search_memory")]
    [Description(
        "Search MiChan's memory database for past interactions, conversations, or information related to a query. Use this when you need to recall previous conversations or find relevant context.")]
    public async Task<string> SearchMemoryAsync(
        [Description("The search query to find relevant memories")]
        string query,
        [Description("Optional: Type of memory to search. Leave empty to search all types.")]
        string? memoryType = null,
        [Description("Optional: The account ID of the user, must be Guid. Leave this blank to search global memories.")]
        Guid? accountId = null,
        [Description("Maximum number of results to return (default: 10)")]
        int limit = 10)
    {
        try
        {
            logger.LogInformation("Searching memory for query: {Query}", query);

            var memories = await memoryService.SearchAsync(
                query: query,
                type: memoryType,
                accountId: accountId,
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
                results.AppendLine(memory.ToPrompt());
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
    /// Get memories by filters
    /// </summary>
    [KernelFunction("get_memories_by_filter")]
    [Description("Retrieve memories by type, account, or other filters. Use this to get specific kinds of memories.")]
    public async Task<string> GetMemoriesByFilterAsync(
        [Description("Optional: Type of memory to filter")]
        string? memoryType = null,
        [Description("Optional: The account ID of the user, must be Guid. Leave this blank to get global memories.")]
        string? accountId = null,
        [Description("Number of memories to retrieve (default: 10)")]
        int count = 10)
    {
        try
        {
            logger.LogInformation("Getting memories by filter: type={MemoryType}", memoryType);

            var memories = await memoryService.GetByFiltersAsync(
                type: memoryType,
                take: count,
                orderBy: "createdAt",
                accountId: accountId is not null ? Guid.Parse(accountId) : Guid.Empty,
                descending: true
            );

            if (memories.Count == 0)
            {
                return "No memories found matching the specified filters.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Found {memories.Count} memories:");
            results.AppendLine();

            foreach (var memory in memories)
            {
                results.AppendLine(memory.ToPrompt());
                results.AppendLine();
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting memories by filter");
            return $"Error retrieving memories: {ex.Message}";
        }
    }

    /// <summary>
    /// Get memories by specific type (with optional semantic filtering)
    /// </summary>
    [KernelFunction("get_memories_by_type")]
    [Description(
        "Retrieve memories filtered by type. Use this to find specific kinds of memories like 'thought' for AI thoughts and chat, or 'autonomous' for autonomous actions.")]
    public async Task<string> GetMemoriesByTypeAsync(
        [Description("The type of memory to retrieve")]
        string memoryType,
        [Description("Optional: A semantic query to filter results further")]
        string? semanticQuery = null,
        [Description("Optional: The account ID of the user, must be Guid. Leave this blank to search global memories.")]
        Guid? accountId = null,
        [Description("Maximum number of results (default: 10)")]
        int limit = 10
    )
    {
        try
        {
            logger.LogInformation("Getting memories of type: {MemoryType}", memoryType);

            List<MiChanMemoryRecord> memories;

            if (!string.IsNullOrWhiteSpace(semanticQuery))
            {
                memories = await memoryService.SearchAsync(
                    query: semanticQuery,
                    type: memoryType,
                    accountId: accountId,
                    limit: limit,
                    minSimilarity: 0.6);
            }
            else
            {
                memories = await memoryService.GetByFiltersAsync(
                    type: memoryType,
                    accountId: accountId,
                    take: limit,
                    orderBy: "createdAt",
                    descending: true);
            }

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
                results.AppendLine(memory.ToPrompt());
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
    /// Store a new memory in the database.
    /// Use this to save important information, facts, or context that should be remembered for future interactions.
    /// </summary>
    [KernelFunction("store_memory")]
    [Description(
        "Store a new memory or fact in the database. Use this to save important information, user preferences, or context that should be remembered for future interactions. The content should be concise and factual. REQUIRED: You must provide the 'content' parameter with the memory text to store.")]
    public async Task<string> StoreMemoryAsync(
        [Description(
            "REQUIRED: The type of memory to store (e.g., 'fact', 'user', 'context', 'summary'). The 'user' will always be loaded in context with the user.")]
        string type,
        [Description("REQUIRED: The content/text to store in the memory. This parameter is mandatory and cannot be empty. Keep it concise, factual, and informative.")]
        string content,
        [Description("Optional: Confidence level 0-1 (default: 0.7)")]
        float? confidence = null,
        [Description(
            "Optional: Account who owns the memory, leave this blank to create global memory. The 'user' type must provide account ID. Type is Guid")]
        Guid? accountId = null,
        [Description("Optional: Mark as hot memory for quick access (default: false)")]
        bool isHot = false
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                return "Error: Content cannot be empty.";

            type = type.ToLower();
            if (string.IsNullOrWhiteSpace(type))
                type = "fact";

            if (type == "user" && accountId is null)
                return "Error: The user type memory must create with the account ID";

            logger.LogInformation("Storing memory of type '{Type}' with content length: {Length}", type,
                content.Length);

            var record = await memoryService.StoreMemoryAsync(
                type: type,
                content: content,
                confidence: confidence ?? 0.7f,
                accountId: accountId,
                hot: isHot
            );

            logger.LogInformation("Successfully stored memory with ID: {MemoryId}", record.Id);

            return $"Memory stored successfully with ID: {record.Id}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing memory of type: {Type}", type);
            return $"Error storing memory: {ex.Message}";
        }
    }

    /// <summary>
    /// Update an existing memory in the database.
    /// Use this to modify content, type, confidence, or hot status of a previously stored memory.
    /// </summary>
    [KernelFunction("update_memory")]
    [Description(
        "Update an existing memory by ID. Use this to correct, expand, or modify previously stored information. Only provide the fields you want to change.")]
    public async Task<string> UpdateMemoryAsync(
        [Description("The ID of the memory to update")]
        string memoryId,
        [Description("Optional: New content for the memory")]
        string? content = null,
        [Description("Optional: New type for the memory")]
        string? type = null,
        [Description("Optional: New confidence level 0-1")]
        float? confidence = null,
        [Description("Optional: Set hot status (true/false)")]
        bool? isHot = null)
    {
        try
        {
            if (!Guid.TryParse(memoryId, out var id))
            {
                return "Error: Invalid memory ID format.";
            }

            logger.LogInformation("Updating memory {MemoryId}", memoryId);

            var record = await memoryService.UpdateAsync(
                id: id,
                content: content,
                type: type,
                confidence: confidence,
                isHot: isHot);

            if (record == null)
            {
                return $"Memory with ID {memoryId} not found or is inactive.";
            }

            logger.LogInformation("Successfully updated memory {MemoryId}", record.Id);

            return $"Memory {record.Id} updated successfully.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating memory: {MemoryId}", memoryId);
            return $"Error updating memory: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a memory from the database (soft delete).
    /// Use this to remove incorrect or outdated information.
    /// </summary>
    [KernelFunction("delete_memory")]
    [Description(
        "Delete a memory by ID. Use this to remove incorrect or outdated information from the memory database.")]
    public async Task<string> DeleteMemoryAsync(
        [Description("The ID of the memory to delete")]
        string memoryId
    )
    {
        try
        {
            if (!Guid.TryParse(memoryId, out var id))
                return "Error: Invalid memory ID format.";

            logger.LogInformation("Deleting memory {MemoryId}", memoryId);

            var success = await memoryService.DeleteAsync(id);

            if (!success)
                return $"Memory with ID {memoryId} not found.";

            logger.LogInformation("Successfully deleted memory {MemoryId}", memoryId);

            return $"Memory {memoryId} deleted successfully.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting memory: {MemoryId}", memoryId);
            return $"Error deleting memory: {ex.Message}";
        }
    }
}