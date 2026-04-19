using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace DysonNetwork.Insight.SnDoc;

/// <summary>
/// Plugin for agents to search and read Solar Network documentation.
/// </summary>
public class SnDocPlugin(
    SnDocService docService,
    ILogger<SnDocPlugin> logger
)
{
    /// <summary>
    /// Search the Solar Network documentation for relevant pages.
    /// Returns a list of matching page IDs with relevance scores.
    /// </summary>
    [KernelFunction("search_docs")]
    [Description("Search the Solar Network documentation for relevant pages using semantic search. Returns page IDs and relevance scores.")]
    public async Task<string> SearchDocsAsync(
        [Description("The search query. Be specific about what you're looking for.")] string query,
        [Description("Maximum number of results to return (default: 5)")] int limit = 5,
        [Description("Minimum similarity threshold from 0.0 to 1.0 (default: 0.6)")] double minSimilarity = 0.6,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Searching docs for query: {Query}", query);

        try
        {
            var results = await docService.SearchAsync(query, limit, minSimilarity, cancellationToken);

            if (results.Count == 0)
            {
                return "No relevant documentation found for your query.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} relevant documentation page(s):");
            sb.AppendLine();

            // Fetch page details for each result
            foreach (var result in results)
            {
                var page = await docService.GetPageByIdAsync(result.PageId, cancellationToken);
                if (page == null) continue;

                sb.AppendLine($"---");
                sb.AppendLine($"Page ID: {result.PageId}");
                sb.AppendLine($"Slug: {page.Slug}");
                sb.AppendLine($"Title: {page.Title}");
                if (!string.IsNullOrEmpty(page.Description))
                {
                    sb.AppendLine($"Description: {page.Description}");
                }
                sb.AppendLine($"Relevance: {result.Similarity:P1}");
                sb.AppendLine($"Content Length: {page.ContentLength:N0} characters");
                sb.AppendLine($"Chunks: {page.ChunkCount}");

                // Include a preview of the best matching chunk content
                var bestChunk = page.Chunks.FirstOrDefault(c => c.ChunkIndex == result.BestChunkIndex);
                if (bestChunk != null)
                {
                    var preview = bestChunk.Content.Length > 300
                        ? bestChunk.Content[..300] + "..."
                        : bestChunk.Content;
                    sb.AppendLine($"Best Match Preview: {preview}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("Use 'read_doc' function with the Page ID to read the full content.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search docs for query: {Query}", query);
            return $"Error searching documentation: {ex.Message}";
        }
    }

    /// <summary>
    /// Read a documentation page by ID with optional pagination.
    /// </summary>
    [KernelFunction("read_doc")]
    [Description("Read a documentation page by its ID. Supports pagination for large documents.")]
    public async Task<string> ReadDocAsync(
        [Description("The Page ID returned from search_docs")] string pageId,
        [Description("Character offset to start reading from (default: 0)")] int offset = 0,
        [Description("Number of characters to read (default: 4000, max: 8000)")] int take = 4000,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Reading doc page: {PageId}, offset: {Offset}, take: {Take}", pageId, offset, take);

        if (!Guid.TryParse(pageId, out var id))
        {
            return $"Invalid Page ID format: {pageId}. Please provide a valid GUID.";
        }

        // Cap take to prevent too large responses
        if (take > 8000) take = 8000;
        if (take < 1) take = 4000;

        try
        {
            var content = await docService.ReadPageAsync(id, offset, take, cancellationToken);

            if (content == null)
            {
                return $"Documentation page with ID '{pageId}' not found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== {content.Title} ===");
            if (!string.IsNullOrEmpty(content.Description))
            {
                sb.AppendLine($"Description: {content.Description}");
            }
            sb.AppendLine($"Slug: {content.Slug}");
            sb.AppendLine($"Reading: characters {content.Offset:N0} - {(content.Offset + content.Taken):N0} of {content.TotalLength:N0}");
            sb.AppendLine();
            sb.AppendLine(content.Content);

            if (content.HasMore)
            {
                sb.AppendLine();
                sb.AppendLine($"---");
                sb.AppendLine($"There is more content available. To continue reading, use:");
                sb.AppendLine($"read_doc(pageId: \"{pageId}\", offset: {content.Offset + content.Taken}, take: {take})");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read doc page: {PageId}", pageId);
            return $"Error reading documentation page: {ex.Message}";
        }
    }

    /// <summary>
    /// Get a quick summary of all available documentation pages.
    /// </summary>
    [KernelFunction("list_docs")]
    [Description("List all available documentation pages with their basic information.")]
    public async Task<string> ListDocsAsync(
        [Description("Maximum number of pages to list (default: 20)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Listing docs with limit: {Limit}", limit);

        try
        {
            var pages = await docService.ListPagesAsync(limit, 0, cancellationToken);

            if (pages.Count == 0)
            {
                return "No documentation pages available.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Available Documentation Pages ({pages.Count} total):");
            sb.AppendLine();

            foreach (var page in pages)
            {
                sb.AppendLine($"---");
                sb.AppendLine($"ID: {page.Id}");
                sb.AppendLine($"Slug: {page.Slug}");
                sb.AppendLine($"Title: {page.Title}");
                if (!string.IsNullOrEmpty(page.Description))
                {
                    var desc = page.Description.Length > 100
                        ? page.Description[..100] + "..."
                        : page.Description;
                    sb.AppendLine($"Description: {desc}");
                }
                sb.AppendLine($"Size: {page.ContentLength:N0} chars, {page.ChunkCount} chunk(s)");
                sb.AppendLine();
            }

            sb.AppendLine("Use 'search_docs' to find specific content or 'read_doc' with a Page ID to read the full content.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list docs");
            return $"Error listing documentation: {ex.Message}";
        }
    }

    /// <summary>
    /// Get a documentation page by its slug.
    /// </summary>
    [KernelFunction("get_doc_by_slug")]
    [Description("Get a documentation page by its slug (URL-friendly identifier). Returns basic info and a preview.")]
    public async Task<string> GetDocBySlugAsync(
        [Description("The slug of the documentation page (e.g., 'getting-started')")] string slug,
        [Description("Number of characters to include in the preview (default: 500)")] int previewLength = 500,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Getting doc by slug: {Slug}", slug);

        try
        {
            var page = await docService.GetPageBySlugAsync(slug, cancellationToken);

            if (page == null)
            {
                return $"Documentation page with slug '{slug}' not found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== {page.Title} ===");
            sb.AppendLine($"ID: {page.Id}");
            sb.AppendLine($"Slug: {page.Slug}");
            if (!string.IsNullOrEmpty(page.Description))
            {
                sb.AppendLine($"Description: {page.Description}");
            }
            sb.AppendLine($"Content Length: {page.ContentLength:N0} characters");
            sb.AppendLine($"Chunks: {page.ChunkCount}");
            sb.AppendLine();

            // Include preview from first chunk
            var preview = page.Content;
            if (preview.Length > previewLength)
            {
                preview = preview[..previewLength] + "...";
            }
            sb.AppendLine("Preview:");
            sb.AppendLine(preview);
            sb.AppendLine();
            sb.AppendLine($"Use 'read_doc(pageId: \"{page.Id}\")' to read the full content.");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get doc by slug: {Slug}", slug);
            return $"Error retrieving documentation: {ex.Message}";
        }
    }
}
