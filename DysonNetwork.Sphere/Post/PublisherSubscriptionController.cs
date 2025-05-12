using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("api/[controller]")]
public class PublisherSubscriptionController(
    PublisherSubscriptionService subs,
    AppDatabase db,
    ILogger<PublisherSubscriptionController> logger
)
    : ControllerBase
{
    public class SubscriptionStatusResponse
    {
        public bool IsSubscribed { get; set; }
        public long PublisherId { get; set; }
        public string PublisherName { get; set; } = string.Empty;
    }

    public class SubscribeRequest
    {
        public int? Tier { get; set; }
    }

    /// <summary>
    /// Check if the current user is subscribed to a publisher
    /// </summary>
    /// <param name="publisherId">Publisher ID to check</param>
    /// <returns>Subscription status</returns>
    [HttpGet("{publisherId}/status")]
    [Authorize]
    public async Task<ActionResult<SubscriptionStatusResponse>> CheckSubscriptionStatus(long publisherId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return NotFound("Publisher not found");

        var isSubscribed = await subs.SubscriptionExistsAsync(currentUser.Id, publisherId);

        return new SubscriptionStatusResponse
        {
            IsSubscribed = isSubscribed,
            PublisherId = publisherId,
            PublisherName = publisher.Name
        };
    }

    /// <summary>
    /// Create or activate a subscription to a publisher
    /// </summary>
    /// <param name="publisherId">Publisher ID to subscribe to</param>
    /// <param name="request">Subscription details</param>
    /// <returns>The created or activated subscription</returns>
    [HttpPost("{publisherId}/subscribe")]
    [Authorize]
    public async Task<ActionResult<PublisherSubscription>> Subscribe(
        long publisherId,
        [FromBody] SubscribeRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return NotFound("Publisher not found");

        try
        {
            var subscription = await subs.CreateSubscriptionAsync(
                currentUser.Id,
                publisherId,
                request.Tier ?? 0
            );

            return subscription;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error subscribing to publisher {PublisherId}", publisherId);
            return StatusCode(500, "Failed to create subscription");
        }
    }

    /// <summary>
    /// Cancel a subscription to a publisher
    /// </summary>
    /// <param name="publisherId">Publisher ID to unsubscribe from</param>
    /// <returns>Success status</returns>
    [HttpPost("{publisherId}/unsubscribe")]
    [Authorize]
    public async Task<ActionResult> Unsubscribe(long publisherId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return NotFound("Publisher not found");

        var success = await subs.CancelSubscriptionAsync(currentUser.Id, publisherId);

        if (success)
            return Ok(new { message = "Subscription cancelled successfully" });

        return NotFound("Active subscription not found");
    }

    /// <summary>
    /// Get all subscriptions for the current user
    /// </summary>
    /// <returns>List of active subscriptions</returns>
    [HttpGet("current")]
    [Authorize]
    public async Task<ActionResult<List<PublisherSubscription>>> GetCurrentSubscriptions()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var subscriptions = await subs.GetAccountSubscriptionsAsync(currentUser.Id);
        return subscriptions;
    }
}