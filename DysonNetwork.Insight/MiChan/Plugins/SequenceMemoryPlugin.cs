using System.ComponentModel;
using System.Text;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Thought;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for searching historic thought sequences as long-term conversation memory.
/// </summary>
public class SequenceMemoryPlugin(
    ThoughtService thoughtService,
    ILogger<SequenceMemoryPlugin> logger)
{
    [AgentTool("search_sequence_memory", Description =
        "Search historic conversation memory from thought sequences. Uses semantic summary search plus keyword part search. Use this when users reference prior conversations, topics, preferences, or long-term context.")]
    public async Task<string> SearchSequenceMemoryAsync(
        [AgentToolParameter("What to search for in historic conversations.")] string query,
        [AgentToolParameter("Optional account id. If provided, searches that account plus public sequences.")] Guid? accountId = null,
        [AgentToolParameter("Maximum results to return (default: 8, max: 20)")] int limit = 8,
        [AgentToolParameter("Minimum semantic similarity threshold (default: 0.6)")] double minSimilarity = 0.6)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Error: query cannot be empty.";
            }

            var results = await thoughtService.SearchSequenceMemoryAsync(
                query,
                accountId,
                Math.Clamp(limit, 1, 20),
                minSimilarity
            );

            if (results.Count == 0)
            {
                return "No relevant sequence memory found.";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Found {results.Count} relevant sequence memories:");
            builder.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var hit = results[i];
                builder.AppendLine($"--- Sequence Memory {i + 1} ---");
                builder.AppendLine($"SequenceId: {hit.SequenceId}");
                builder.AppendLine($"AccountId: {hit.AccountId}");
                builder.AppendLine($"LastMessageAt: {hit.LastMessageAt}");
                builder.AppendLine($"MatchType: {hit.MatchType}");
                if (hit.Similarity.HasValue)
                {
                    builder.AppendLine($"Similarity: {hit.Similarity.Value:F4}");
                }

                if (!string.IsNullOrWhiteSpace(hit.Topic))
                {
                    builder.AppendLine($"Topic: {hit.Topic}");
                }

                if (!string.IsNullOrWhiteSpace(hit.Summary))
                {
                    builder.AppendLine($"Summary: {hit.Summary}");
                }

                if (!string.IsNullOrWhiteSpace(hit.TextSnippet))
                {
                    builder.AppendLine($"Snippet: {hit.TextSnippet}");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching sequence memory with query: {Query}", query);
            return $"Error searching sequence memory: {ex.Message}";
        }
    }
}
