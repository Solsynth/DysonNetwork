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
    /// Search through MiChan's memory for information related to a query.
    /// This searches ALL shared memories by default - use this often to recall what you know!
    /// </summary>
    [KernelFunction("search_memory")]
    [Description(
        "Search your memory database for relevant information. USE THIS FREQUENTLY before responding - you have access to shared memories from ALL conversations! By default (no accountId), this searches global memories visible to everyone. Use this to recall user preferences, past topics, facts, and context to make your responses more personal and consistent.")]
    public async Task<string> SearchMemoryAsync(
        [Description("What to search for. Be specific: 'user's favorite color', 'discussions about AI', 'Alice's job'")]
        string query,
        [Description("Optional: Filter by memory type ('user', 'topic', 'fact', 'context', 'interaction'). Leave empty to search all.")]
        string? memoryType = null,
        [Description("Optional: Account ID to include that user's specific memories too. Leave EMPTY to search only global shared memories (recommended for most searches).")]
        Guid? accountId = null,
        [Description("Maximum results to return (default: 10)")]
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

            for (var i = 0; i < memories.Count; i++)
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
    /// Use this FREQUENTLY to save information - this is how you learn and build up knowledge about the world and users.
    /// </summary>
    [KernelFunction("store_memory")]
    [Description(
        "Store a new memory or fact. USE THIS OFTEN - every interaction is an opportunity to learn! Save user preferences, facts, topics discussed, insights, observations, and any information that would be useful later. Memories are SHARED across all users, helping you be consistent and knowledgeable with everyone. Be prolific - store multiple memories from each conversation.")]
    public async Task<string> StoreMemoryAsync(
        [Description(
            "REQUIRED. The type of memory: 'user' (user info/personality/preferences - requires accountId), 'topic' (subjects discussed), 'fact' (general knowledge), 'context' (situation details), 'interaction' (social patterns). Choose the best fit.")]
        string type,
        [Description("REQUIRED. The actual content to remember. Be specific and detailed. Example: 'User Alice is passionate about machine learning and works at Google' or 'We discussed the importance of sleep for productivity'")]
        string content,
        [Description("Optional: How confident you are in this memory 0-1 (default: 0.7). Higher for facts, lower for observations.")]
        float? confidence = null,
        [Description(
            "Optional: Account ID (Guid) if this is user-specific. Leave EMPTY to create GLOBAL memory visible to all conversations. Most memories should be global!")]
        Guid? accountId = null,
        [Description("Optional: Mark as 'hot' for frequently accessed memories (default: false)")]
        bool? isHot = null
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
                return "Error: Content cannot be empty.";

            if (string.IsNullOrWhiteSpace(type))
                return "Error: Type cannot be empty.";

            type = type.ToLower();

            if (type == "user" && accountId is null)
                return "Error: The user type memory must create with the account ID";

            logger.LogInformation("Storing memory of type '{Type}' with content length: {Length}", type,
                content.Length);

            var record = await memoryService.StoreMemoryAsync(
                type: type,
                content: content,
                confidence: confidence ?? 0.7f,
                accountId: accountId,
                hot: isHot ?? false
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