using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
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
        public Guid PublisherId { get; set; }
        public string PublisherName { get; set; } = string.Empty;
    }

    public class SubscribeRequest
    {
        public int? Tier { get; set; }
    }

    [HttpGet("{name}/subscription")]
    [Authorize]
    public async Task<ActionResult<SubscriptionStatusResponse>> CheckSubscriptionStatus(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
    
        // Check if the publisher exists
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");
    
        var isSubscribed = await subs.SubscriptionExistsAsync(Guid.Parse(currentUser.Id), publisher.Id);
    
        return new SubscriptionStatusResponse
        {
            IsSubscribed = isSubscribed,
            PublisherId = publisher.Id,
            PublisherName = publisher.Name
        };
    }

    [HttpPost("{name}/subscribe")]
    [Authorize]
    public async Task<ActionResult<SnPublisherSubscription>> Subscribe(
        string name,
        [FromBody] SubscribeRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
    
        // Check if the publisher exists
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");
    
        try
        {
            var subscription = await subs.CreateSubscriptionAsync(
                Guid.Parse(currentUser.Id),
                publisher.Id,
                request.Tier ?? 0
            );
    
            return subscription;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error subscribing to publisher {PublisherName}", name);
            return StatusCode(500, "Failed to create subscription");
        }
    }

    [HttpPost("{name}/unsubscribe")]
    [Authorize]
    public async Task<ActionResult> Unsubscribe(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var success = await subs.CancelSubscriptionAsync(Guid.Parse(currentUser.Id), publisher.Id);

        if (success)
            return Ok(new { message = "Subscription cancelled successfully" });

        return NotFound("Active subscription not found");
    }

    /// <summary>
    /// Get all subscriptions for the current user
    /// </summary>
    /// <returns>List of active subscriptions</returns>
    [HttpGet("subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<SnPublisherSubscription>>> GetCurrentSubscriptions()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var subscriptions = await subs.GetAccountSubscriptionsAsync(Guid.Parse(currentUser.Id));
        return subscriptions;
    }
}