using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

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
    public class SubscribeRequest
    {
    }

    public class UpdateSubscriptionReadStateRequest
    {
        public Instant? LastReadAt { get; set; }
    }

    [HttpGet("{name}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnPublisherSubscription>> CheckSubscriptionStatus(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var subscription = await subs.GetSubscriptionAsync(Guid.Parse(currentUser.Id), publisher.Id);
        if (subscription is null) return NotFound("Subscription not found");
        return subscription;
    }

    [HttpGet("{name}/subscription/read-status")]
    [Authorize]
    public async Task<ActionResult<PublisherSubscriptionService.SubscriptionReadStatus>>
        GetSubscriptionReadStatus(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var status = await subs.GetSubscriptionReadStatusAsync(Guid.Parse(currentUser.Id), publisher.Id);
        if (status is null)
            return NotFound("Subscription not found");

        return Ok(status);
    }

    [HttpPost("{name}/subscribe")]
    [Authorize]
    public async Task<ActionResult<SnPublisherSubscription>> Subscribe(
        string name,
        [FromBody] SubscribeRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        // Check if the publisher exists
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        try
        {
            var subscription = await subs.CreateSubscriptionAsync(
                Guid.Parse(currentUser.Id),
                publisher.Id
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
    /// <returns>List of active subscriptions with IsLive flag</returns>
    [HttpGet("subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<SubscriptionWithLiveStatus>>> GetCurrentSubscriptions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var subscriptions = await db.PublisherSubscriptions
            .Include(ps => ps.Publisher)
            .Where(ps => ps.AccountId == accountId)
            .OrderByDescending(ps => ps.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var totalCount = await db.PublisherSubscriptions
            .CountAsync(ps => ps.AccountId == accountId);

        var publisherIds = subscriptions.Select(s => s.PublisherId).ToList();

        var livePublisherIds = await db.LiveStreams
            .Where(ls => publisherIds.Contains(ls.PublisherId ?? Guid.Empty)
                      && ls.Status == Shared.Models.LiveStreamStatus.Active)
            .Select(ls => ls.PublisherId)
            .Distinct()
            .ToListAsync();

        var latestContentAt = await subs.GetLatestContentAtForPublishersAsync(publisherIds);

        var result = subscriptions.Select(ps => new SubscriptionWithLiveStatus
        {
            Subscription = ps,
            IsLive = livePublisherIds.Contains(ps.PublisherId),
            LatestContentAt = latestContentAt.GetValueOrDefault(ps.PublisherId),
            HasNewContent = latestContentAt.TryGetValue(ps.PublisherId, out var publisherLatestContentAt) &&
                            publisherLatestContentAt != null &&
                            (ps.LastReadAt == null || publisherLatestContentAt > ps.LastReadAt)
        }).ToList();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(result);
    }

    public class SubscriptionWithLiveStatus
    {
        public SnPublisherSubscription Subscription { get; set; } = null!;
        public bool IsLive { get; set; }
        public Instant? LatestContentAt { get; set; }
        public bool HasNewContent { get; set; }
    }

    [HttpPut("{name}/subscription/read-status")]
    [Authorize]
    public async Task<ActionResult<PublisherSubscriptionService.SubscriptionReadStatus>>
        UpdateSubscriptionReadStatus(string name, [FromBody] UpdateSubscriptionReadStateRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var subscription = await subs.UpdateLastReadAtAsync(
            Guid.Parse(currentUser.Id),
            publisher.Id,
            request.LastReadAt
        );
        if (subscription is null)
            return NotFound("Subscription not found");

        var status = await subs.GetSubscriptionReadStatusAsync(Guid.Parse(currentUser.Id), publisher.Id);
        if (status is null)
            return NotFound("Subscription not found");

        return Ok(status);
    }

    /// <summary>
    /// Get active live streams from publishers the current user is subscribed to
    /// </summary>
    /// <returns>List of active live streams from subscribed publishers</returns>
    [HttpGet("subscriptions/live")]
    [Authorize]
    public async Task<ActionResult<List<SnLiveStream>>> GetSubscribedPublisherLiveStreams(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Get subscribed publisher IDs
        var subscribedPublisherIds = await db.PublisherSubscriptions
            .Where(ps => ps.AccountId == accountId)
            .Select(ps => ps.PublisherId)
            .ToListAsync();

        if (!subscribedPublisherIds.Any())
        {
            return Ok(new List<SnLiveStream>());
        }

        // Get active live streams from subscribed publishers
        var liveStreams = await db.LiveStreams
            .Include(ls => ls.Publisher)
            .Where(ls => subscribedPublisherIds.Contains(ls.PublisherId ?? Guid.Empty) 
                      && ls.Status == Shared.Models.LiveStreamStatus.Active
                      && ls.Visibility == Shared.Models.LiveStreamVisibility.Public)
            .OrderByDescending(ls => ls.StartedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(liveStreams);
    }
}
