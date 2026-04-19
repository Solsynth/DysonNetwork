using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Insight.SnDoc;

[ApiController]
[Route("/api/docs")]
public class SnDocController(
    SnDocService docService,
    ILogger<SnDocController> logger
) : ControllerBase
{
    #region DTOs

    public class CreatePageRequest
    {
        public string Slug { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string Content { get; set; } = null!;
    }

    public class PageResponse
    {
        public Guid Id { get; set; }
        public string Slug { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public int ContentLength { get; set; }
        public int ChunkCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PageDetailResponse : PageResponse
    {
        public string Content { get; set; } = null!;
    }

    public class PageContentResponse
    {
        public Guid PageId { get; set; }
        public string Slug { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string Content { get; set; } = null!;
        public int TotalLength { get; set; }
        public int Offset { get; set; }
        public int Taken { get; set; }
        public bool HasMore { get; set; }
        public int ChunkCount { get; set; }
    }

    public class SearchRequest
    {
        public string Query { get; set; } = null!;
        public int Limit { get; set; } = 10;
        public double? MinSimilarity { get; set; } = 0.6;
    }

    public class SearchResultResponse
    {
        public Guid PageId { get; set; }
        public string Slug { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public double Similarity { get; set; }
        public int BestChunkIndex { get; set; }
        public List<int> RelevantChunkIndices { get; set; } = [];
    }

    public class SearchResponse
    {
        public string Query { get; set; } = null!;
        public List<SearchResultResponse> Results { get; set; } = [];
        public int TotalCount { get; set; }
    }

    public class ListPagesResponse
    {
        public List<PageResponse> Pages { get; set; } = [];
        public int TotalCount { get; set; }
    }

    #endregion

    /// <summary>
    /// Create a new documentation page or update an existing one.
    /// Requires 'docs.write' permission.
    /// </summary>
    [HttpPost("pages")]
    [AskPermission("docs.write")]
    [ProducesResponseType(typeof(PageDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PageDetailResponse>> CreateOrUpdatePage(
        [FromBody] CreatePageRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return BadRequest(new { Error = "Slug is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { Error = "Title is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { Error = "Content is required" });
        }

        try
        {
            var page = await docService.UpsertPageAsync(
                request.Slug,
                request.Title,
                request.Description,
                request.Content,
                ct);

            return Ok(MapToDetailResponse(page));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create/update doc page: {Slug}", request.Slug);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { Error = "Failed to process documentation page" });
        }
    }

    /// <summary>
    /// Get a documentation page by slug.
    /// Slug can contain slashes (e.g., "api/v2/authentication").
    /// Public endpoint - no authentication required for reading.
    /// </summary>
    [HttpGet("pages/slug/{**slug}")]
    [ProducesResponseType(typeof(PageDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PageDetailResponse>> GetPageBySlug(
        string slug,
        CancellationToken ct)
    {
        var page = await docService.GetPageBySlugAsync(slug, ct);

        if (page == null)
        {
            return NotFound(new { Error = $"Documentation page '{slug}' not found" });
        }

        return Ok(MapToDetailResponse(page));
    }

    /// <summary>
    /// Get a documentation page by ID.
    /// Public endpoint - no authentication required for reading.
    /// </summary>
    [HttpGet("pages/{id:guid}")]
    [ProducesResponseType(typeof(PageDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PageDetailResponse>> GetPageById(
        Guid id,
        CancellationToken ct)
    {
        var page = await docService.GetPageByIdAsync(id, ct);

        if (page == null)
        {
            return NotFound(new { Error = $"Documentation page '{id}' not found" });
        }

        return Ok(MapToDetailResponse(page));
    }

    /// <summary>
    /// Read a documentation page content with pagination support.
    /// Public endpoint - no authentication required for reading.
    /// </summary>
    [HttpGet("pages/{pageId:guid}/content")]
    [ProducesResponseType(typeof(PageContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PageContentResponse>> ReadPageContent(
        Guid pageId,
        [FromQuery] int? offset,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var content = await docService.ReadPageAsync(pageId, offset, take, ct);

        if (content == null)
        {
            return NotFound(new { Error = "Documentation page not found" });
        }

        return Ok(new PageContentResponse
        {
            PageId = content.PageId,
            Slug = content.Slug,
            Title = content.Title,
            Description = content.Description,
            Content = content.Content,
            TotalLength = content.TotalLength,
            Offset = content.Offset,
            Taken = content.Taken,
            HasMore = content.HasMore,
            ChunkCount = content.ChunkCount
        });
    }

    /// <summary>
    /// Delete a documentation page by slug.
    /// Slug can contain slashes (e.g., "api/v2/authentication").
    /// Requires 'docs.write' permission.
    /// </summary>
    [HttpDelete("pages/slug/{**slug}")]
    [AskPermission("docs.write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeletePageBySlug(string slug, CancellationToken ct)
    {
        var deleted = await docService.DeletePageAsync(slug, ct);

        if (!deleted)
        {
            return NotFound(new { Error = $"Documentation page '{slug}' not found" });
        }

        return NoContent();
    }

    /// <summary>
    /// Delete a documentation page by ID.
    /// Requires 'docs.write' permission.
    /// </summary>
    [HttpDelete("pages/{id:guid}")]
    [AskPermission("docs.write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeletePageById(Guid id, CancellationToken ct)
    {
        // Get page by ID to find its slug
        var page = await docService.GetPageByIdAsync(id, ct);
        if (page == null)
        {
            return NotFound(new { Error = $"Documentation page '{id}' not found" });
        }

        var deleted = await docService.DeletePageAsync(page.Slug, ct);

        if (!deleted)
        {
            return NotFound(new { Error = $"Documentation page '{id}' not found" });
        }

        return NoContent();
    }

    /// <summary>
    /// List all documentation pages (summary info only).
    /// Public endpoint - no authentication required for reading.
    /// </summary>
    [HttpGet("pages")]
    [ProducesResponseType(typeof(ListPagesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListPagesResponse>> ListPages(
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken ct)
    {
        var pages = await docService.ListPagesAsync(limit, offset, ct);

        return Ok(new ListPagesResponse
        {
            Pages = pages.Select(MapToResponse).ToList(),
            TotalCount = pages.Count
        });
    }

    /// <summary>
    /// Search documentation pages using semantic search.
    /// Public endpoint - no authentication required for reading.
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchResponse>> SearchDocs(
        [FromBody] SearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { Error = "Query is required" });
        }

        try
        {
            var results = await docService.SearchAsync(
                request.Query,
                request.Limit,
                request.MinSimilarity,
                ct);

            // Fetch page details for each result
            var pageIds = results.Select(r => r.PageId).Distinct().ToList();
            var pageDetails = new Dictionary<Guid, SnDocPageSummary>();

            foreach (var pageId in pageIds)
            {
                var page = await docService.GetPageByIdAsync(pageId, ct);
                if (page != null)
                {
                    pageDetails[pageId] = new SnDocPageSummary
                    {
                        Id = page.Id,
                        Slug = page.Slug,
                        Title = page.Title,
                        Description = page.Description,
                        ContentLength = page.ContentLength,
                        ChunkCount = page.ChunkCount,
                        CreatedAt = page.CreatedAt,
                        UpdatedAt = page.UpdatedAt
                    };
                }
            }

            var responseResults = results
                .Where(r => pageDetails.ContainsKey(r.PageId))
                .Select(r =>
                {
                    var page = pageDetails[r.PageId];
                    return new SearchResultResponse
                    {
                        PageId = r.PageId,
                        Slug = page.Slug,
                        Title = page.Title,
                        Description = page.Description,
                        Similarity = r.Similarity,
                        BestChunkIndex = r.BestChunkIndex,
                        RelevantChunkIndices = r.RelevantChunkIndices
                    };
                })
                .ToList();

            return Ok(new SearchResponse
            {
                Query = request.Query,
                Results = responseResults,
                TotalCount = responseResults.Count
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search docs: {Query}", request.Query);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { Error = "Failed to search documentation" });
        }
    }

    #region Mapping Helpers

    private static PageResponse MapToResponse(SnDocPageSummary page)
    {
        return new PageResponse
        {
            Id = page.Id,
            Slug = page.Slug,
            Title = page.Title,
            Description = page.Description,
            ContentLength = page.ContentLength,
            ChunkCount = page.ChunkCount,
            CreatedAt = page.CreatedAt.ToDateTimeUtc(),
            UpdatedAt = page.UpdatedAt.ToDateTimeUtc()
        };
    }

    private static PageDetailResponse MapToDetailResponse(SnDocPage page)
    {
        return new PageDetailResponse
        {
            Id = page.Id,
            Slug = page.Slug,
            Title = page.Title,
            Description = page.Description,
            Content = page.Content,
            ContentLength = page.ContentLength,
            ChunkCount = page.ChunkCount,
            CreatedAt = page.CreatedAt.ToDateTimeUtc(),
            UpdatedAt = page.UpdatedAt.ToDateTimeUtc()
        };
    }

    #endregion
}
