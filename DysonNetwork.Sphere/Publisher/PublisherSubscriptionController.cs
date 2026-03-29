using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

[ApiController]
[Route("/api/publishers")]
public class PublisherSubscriptionController(
    PublisherSubscriptionService subs,
    PublisherService pub,
    RemoteRingService ring,
    RemoteAccountService accounts,
    ILocalizationService localization,
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

    public class SubscriptionStatusResponse
    {
        public SnPublisherSubscription? Subscription { get; set; }
        public SnPublisherFollowRequest? FollowRequest { get; set; }
        public bool RequiresApproval { get; set; }
        public string Status { get; set; } = "none";
        public string Message { get; set; } = string.Empty;
    }

    [HttpGet("{name}/subscription")]
    [Authorize]
    public async Task<ActionResult<SubscriptionStatusResponse>> CheckSubscriptionStatus(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        var requiresApproval = await pub.HasFollowRequiresApprovalFlag(publisher.Id);

        var response = new SubscriptionStatusResponse
        {
            RequiresApproval = requiresApproval
        };

        if (requiresApproval)
        {
            var followRequest = await pub.GetFollowRequest(publisher.Id, accountId);
            response.FollowRequest = followRequest;

            if (followRequest != null)
            {
                response.Subscription = await subs.GetSubscriptionAsync(accountId, publisher.Id);
                response.Status = followRequest.State switch
                {
                    FollowRequestState.Pending => "pending",
                    FollowRequestState.Accepted => "following",
                    FollowRequestState.Rejected => "rejected",
                    _ => "none"
                };
                response.Message = followRequest.State switch
                {
                    FollowRequestState.Pending => "Follow request is pending approval",
                    FollowRequestState.Accepted => "You are following this publisher",
                    FollowRequestState.Rejected => "Follow request was rejected",
                    _ => string.Empty
                };
                return Ok(response);
            }
        }

        var subscription = await subs.GetSubscriptionAsync(accountId, publisher.Id);
        if (subscription != null)
        {
            response.Subscription = subscription;
            response.Status = "subscribed";
            response.Message = "You are subscribed to this publisher";
        }

        return Ok(response);
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
    public async Task<ActionResult<SubscriptionStatusResponse>> Subscribe(
        string name,
        [FromBody] SubscribeRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        var requiresApproval = await pub.HasFollowRequiresApprovalFlag(publisher.Id);

        if (requiresApproval)
        {
            var existingRequest = await pub.GetFollowRequest(publisher.Id, accountId);
            if (existingRequest != null)
            {
                return existingRequest.State switch
                {
                    FollowRequestState.Pending => BadRequest(new SubscriptionStatusResponse
                    {
                        Status = "pending",
                        Message = "Follow request already pending",
                        RequiresApproval = true,
                        FollowRequest = existingRequest
                    }),
                    FollowRequestState.Accepted => Ok(new SubscriptionStatusResponse
                    {
                        Status = "following",
                        Message = "Already following",
                        RequiresApproval = true,
                        FollowRequest = existingRequest
                    }),
                    FollowRequestState.Rejected => BadRequest(new SubscriptionStatusResponse
                    {
                        Status = "rejected",
                        Message = "Follow request was rejected. Please contact the publisher.",
                        RequiresApproval = true,
                        FollowRequest = existingRequest
                    }),
                    _ => BadRequest("Invalid follow request state")
                };
            }

            var followRequest = await pub.CreateFollowRequest(publisher.Id, accountId);

            var title = localization.Get("followRequestReceivedTitle", currentUser.Language,
                new { publisher = publisher.Nick });
            var body = localization.Get("followRequestReceivedBody", currentUser.Language,
                new { publisher = publisher.Nick });

            var managerMembers = await pub.GetPublisherMembers(publisher.Id);
            foreach (var manager in managerMembers.Where(m => m.Role >= PublisherMemberRole.Manager))
            {
                var managerAccount = await accounts.GetAccount(manager.AccountId);
                if (managerAccount != null)
                {
                    await ring.SendPushNotificationToUser(
                        manager.AccountId.ToString(),
                        "follow_request",
                        title,
                        null,
                        body,
                        isSavable: true
                    );
                }
            }

            return Ok(new SubscriptionStatusResponse
            {
                Status = "pending",
                Message = "Follow request submitted and pending approval",
                RequiresApproval = true,
                FollowRequest = followRequest
            });
        }
        else
        {
            var existingSubscription = await subs.GetSubscriptionAsync(accountId, publisher.Id);
            if (existingSubscription != null)
                return BadRequest(new SubscriptionStatusResponse
                {
                    Status = "subscribed",
                    Message = "Already subscribed to this publisher",
                    Subscription = existingSubscription
                });

            try
            {
                var subscription = await subs.CreateSubscriptionAsync(accountId, publisher.Id);
                return Ok(new SubscriptionStatusResponse
                {
                    Status = "subscribed",
                    Message = "Successfully subscribed",
                    Subscription = subscription
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error subscribing to publisher {PublisherName}", name);
                return StatusCode(500, "Failed to create subscription");
            }
        }
    }

    [HttpPost("{name}/unsubscribe")]
    [Authorize]
    public async Task<ActionResult<SubscriptionStatusResponse>> Unsubscribe(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        var requiresApproval = await pub.HasFollowRequiresApprovalFlag(publisher.Id);

        if (requiresApproval)
        {
            var existingRequest = await pub.GetFollowRequest(publisher.Id, accountId);
            if (existingRequest != null)
            {
                await pub.CancelFollowRequest(publisher.Id, accountId);
                return Ok(new SubscriptionStatusResponse
                {
                    Status = "none",
                    Message = "Follow request cancelled",
                    RequiresApproval = true,
                    FollowRequest = null
                });
            }
            return NotFound(new SubscriptionStatusResponse
            {
                Status = "none",
                Message = "No follow request found",
                RequiresApproval = true
            });
        }
        else
        {
            var success = await subs.CancelSubscriptionAsync(accountId, publisher.Id);
            if (success)
                return Ok(new SubscriptionStatusResponse
                {
                    Status = "none",
                    Message = "Subscription cancelled successfully"
                });

            return NotFound(new SubscriptionStatusResponse
            {
                Status = "none",
                Message = "No active subscription found"
            });
        }
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

        var subscribedPublisherIds = await db.PublisherSubscriptions
            .Where(ps => ps.AccountId == accountId)
            .Select(ps => ps.PublisherId)
            .ToListAsync();

        if (!subscribedPublisherIds.Any())
        {
            return Ok(new List<SnLiveStream>());
        }

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

    public class RejectFollowRequestBody
    {
        public string? Reason { get; set; }
    }

    [HttpGet("{name}/subscription/requests")]
    [Authorize]
    public async Task<ActionResult<List<SnPublisherFollowRequest>>> GetPendingFollowRequests(string name)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return Forbid();

        var requests = await pub.GetPendingFollowRequests(publisher.Id);
        return Ok(requests);
    }

    [HttpPost("{name}/subscription/requests/{requestId}/approve")]
    [Authorize]
    public async Task<ActionResult<SnPublisherFollowRequest>> ApproveFollowRequest(string name, Guid requestId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return Forbid();

        try
        {
            var request = await pub.ApproveFollowRequest(requestId, accountId);

            var requesterAccount = await accounts.GetAccount(request.AccountId);
            if (requesterAccount != null)
            {
                var title = localization.Get("followRequestApprovedTitle", requesterAccount.Language);
                var body = localization.Get("followRequestApprovedBody", requesterAccount.Language,
                    new { publisher = publisher.Nick });

                await ring.SendPushNotificationToUser(
                    request.AccountId.ToString(),
                    "follow_approved",
                    title,
                    null,
                    body,
                    isSavable: true
                );
            }

            return Ok(request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{name}/subscription/requests/{requestId}/reject")]
    [Authorize]
    public async Task<ActionResult<SnPublisherFollowRequest>> RejectFollowRequest(string name, Guid requestId,
        [FromBody] RejectFollowRequestBody body)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return Forbid();

        try
        {
            var request = await pub.RejectFollowRequest(requestId, accountId, body.Reason);

            var requesterAccount = await accounts.GetAccount(request.AccountId);
            if (requesterAccount != null)
            {
                var title = localization.Get("followRequestRejectedTitle", requesterAccount.Language);
                var notificationBody = localization.Get("followRequestRejectedBody", requesterAccount.Language,
                    new { publisher = publisher.Nick });

                await ring.SendPushNotificationToUser(
                    request.AccountId.ToString(),
                    "follow_rejected",
                    title,
                    null,
                    notificationBody,
                    isSavable: true
                );
            }

            return Ok(request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
