using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.WebReader;

[ApiController]
[Route("/api/feeds")]
public class WebFeedPublicController(
    AppDatabase db
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
        var subscription = new WebFeedSubscription
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
    public async Task<ActionResult<WebFeedSubscription>> GetSubscriptionStatus(Guid feedId)
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
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMySubscriptions(
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
            .ThenInclude(f => f.Publisher)
            .OrderByDescending(s => s.CreatedAt);

        var totalCount = await query.CountAsync();
        var subscriptions = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(subscriptions);
    }
}