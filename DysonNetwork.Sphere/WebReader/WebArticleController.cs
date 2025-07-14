using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.WebReader;

[ApiController]
[Route("/api/feeds/articles")]
public class WebArticleController(AppDatabase db) : ControllerBase
{
    /// <summary>
    /// Get a list of recent web articles
    /// </summary>
    /// <param name="limit">Maximum number of articles to return</param>
    /// <param name="offset">Number of articles to skip</param>
    /// <param name="feedId">Optional feed ID to filter by</param>
    /// <param name="publisherId">Optional publisher ID to filter by</param>
    /// <returns>List of web articles</returns>
    [HttpGet]
    public async Task<IActionResult> GetArticles(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] Guid? feedId = null,
        [FromQuery] Guid? publisherId = null
    )
    {
        var query = db.WebArticles
            .OrderByDescending(a => a.PublishedAt)
            .Include(a => a.Feed)
            .AsQueryable();

        if (feedId.HasValue)
            query = query.Where(a => a.FeedId == feedId.Value);
        if (publisherId.HasValue)
            query = query.Where(a => a.Feed.PublisherId == publisherId.Value);

        var totalCount = await query.CountAsync();
        var articles = await query
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        
        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(articles);
    }

    /// <summary>
    /// Get a specific web article by ID
    /// </summary>
    /// <param name="id">The article ID</param>
    /// <returns>The web article</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetArticle(Guid id)
    {
        var article = await db.WebArticles
            .Include(a => a.Feed)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null)
            return NotFound();
        
        return Ok(article);
    }

    /// <summary>
    /// Get random web articles
    /// </summary>
    /// <param name="limit">Maximum number of articles to return</param>
    /// <returns>List of random web articles</returns>
    [HttpGet("random")]
    public async Task<IActionResult> GetRandomArticles([FromQuery] int limit = 5)
    {
        var articles = await db.WebArticles
            .OrderBy(_ => EF.Functions.Random())
            .Include(a => a.Feed)
            .Take(limit)
            .ToListAsync();

        return Ok(articles);
    }
}