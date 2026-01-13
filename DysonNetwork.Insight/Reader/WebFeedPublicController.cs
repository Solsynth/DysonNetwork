using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Insight.Reader;

[ApiController]
[Route("/api/feeds")]
public class WebFeedPublicController(
    AppDatabase db,
    WebFeedService webFeed
) : ControllerBase
{
    /// <summary>
    /// Subscribe to a web feed
    /// </summary>
    /// <param name="feedId">The ID of the feed to subscribe to</param>
    /// <returns>Subscription details</returns>
    [HttpPost("{feedId:guid}/subscribe")]
    [Authorize]
    public async Task<IActionResult> Subscribe(Guid feedId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        // Check if feed exists
        var feed = await db.WebFeeds.FindAsync(feedId);
        if (feed == null)
            return NotFound("Feed not found");

        // Check if already subscribed
        var existingSubscription = await db.WebFeedSubscriptions
            .FirstOrDefaultAsync(s => s.FeedId == feedId && s.AccountId == accountId);

        if (existingSubscription != null)
            return Ok(existingSubscription);

        // Create new subscription
        var subscription = new SnWebFeedSubscription
        {
            FeedId = feedId,
            AccountId = accountId
        };

        db.WebFeedSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSubscriptionStatus), new { feedId }, subscription);
    }

    /// <summary>
    /// Unsubscribe from a web feed
    /// </summary>
    /// <param name="feedId">The ID of the feed to unsubscribe from</param>
    [HttpDelete("{feedId:guid}/subscribe")]
    [Authorize]
    public async Task<IActionResult> Unsubscribe(Guid feedId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var subscription = await db.WebFeedSubscriptions
            .FirstOrDefaultAsync(s => s.FeedId == feedId && s.AccountId == accountId);

        if (subscription == null)
            return NoContent();

        db.WebFeedSubscriptions.Remove(subscription);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Get subscription status for the current user and a specific feed
    /// </summary>
    /// <param name="feedId">The ID of the feed to check</param>
    /// <returns>Subscription status</returns>
    [HttpGet("{feedId:guid}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnWebFeedSubscription>> GetSubscriptionStatus(Guid feedId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var subscription = await db.WebFeedSubscriptions
            .Where(s => s.FeedId == feedId && s.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (subscription is null)
            return NotFound();

        return Ok(subscription);
    }

    /// <summary>
    /// List all feeds the current user is subscribed to
    /// </summary>
    /// <returns>List of subscribed feeds</returns>
    [HttpGet("subscribed")]
    [Authorize]
    public async Task<ActionResult<SnWebFeed>> GetSubscribedFeeds(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var query = db.WebFeedSubscriptions
            .Where(s => s.AccountId == accountId)
            .Include(s => s.Feed)
            .OrderByDescending(s => s.CreatedAt);

        var totalCount = await query.CountAsync();
        var subscriptions = await query
            .Select(q => q.Feed)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(subscriptions);
    }

    /// <summary>
    /// Get articles from subscribed feeds
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<SnWebFeed>> GetWebFeedArticles(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var subscribedFeedIds = await db.WebFeedSubscriptions
            .Where(s => s.AccountId == accountId)
            .Select(s => s.FeedId)
            .ToListAsync();

        var query = db.WebFeeds
            .Where(f => subscribedFeedIds.Contains(f.Id))
            .OrderByDescending(f => f.CreatedAt);

        var totalCount = await query.CountAsync();
        var feeds = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(feeds);
    }

    /// <summary>
    /// Get feed metadata by ID (public endpoint)
    /// </summary>
    /// <param name="feedId">The ID of the feed</param>
    /// <returns>Feed metadata</returns>
    [AllowAnonymous]
    [HttpGet("{feedId:guid}")]
    public async Task<ActionResult<SnWebFeed>> GetFeedById(Guid feedId)
    {
        var feed = await webFeed.GetFeedAsync(feedId);
        if (feed == null)
            return NotFound();

        return Ok(feed);
    }

    /// <summary>
    /// Get articles from a specific feed (public endpoint)
    /// </summary>
    /// <param name="feedId">The ID of the feed</param>
    /// <param name="offset">Number of articles to skip</param>
    /// <param name="take">Maximum number of articles to return</param>
    /// <returns>List of articles from the feed</returns>
    [AllowAnonymous]
    [HttpGet("{feedId:guid}/articles")]
    public async Task<ActionResult<SnWebArticle>> GetFeedArticles(
        [FromRoute] Guid feedId,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        // Check if feed exists
        var feedExists = await db.WebFeeds.AnyAsync(f => f.Id == feedId);
        if (!feedExists)
            return NotFound("Feed not found");

        var query = db.WebArticles
            .Where(a => a.FeedId == feedId)
            .OrderByDescending(a => a.CreatedAt)
            .Include(a => a.Feed);

        var totalCount = await query.CountAsync();
        var articles = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(articles);
    }

    /// <summary>
    /// Explore available web feeds
    /// </summary>
    [HttpGet("explore")]
    [Authorize]
    public async Task<ActionResult<SnWebFeed>> ExploreFeeds(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? query = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var feedsQuery = db.WebFeeds
            .OrderByDescending(f => f.CreatedAt)
            .AsQueryable();

        // Apply search filter if query is provided
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchTerm = $"%{query}%";
            feedsQuery = feedsQuery.Where(f =>
                EF.Functions.ILike(f.Title, searchTerm) ||
                (f.Description != null && EF.Functions.ILike(f.Description, searchTerm))
            );
        }

        var totalCount = await feedsQuery.CountAsync();
        var feeds = await feedsQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(feeds);
    }
}