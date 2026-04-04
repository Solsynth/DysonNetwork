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
        public bool IsPending { get; set; }
        public bool IsActive { get; set; }
        public bool Notify { get; set; } = true;
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
                var followSubscription = await subs.GetSubscriptionIncludingEndedAsync(accountId, publisher.Id);
                response.Subscription = followSubscription;
                response.Notify = followSubscription?.Notify ?? true;
                response.IsActive = followRequest.State == FollowRequestState.Accepted && (followSubscription?.IsActive ?? false);
                response.IsPending = followRequest.State == FollowRequestState.Pending;
                response.Status = followRequest.State switch
                {
                    FollowRequestState.Pending => "pending",
                    FollowRequestState.Accepted => response.IsActive ? "following" : "ended",
                    FollowRequestState.Rejected => "rejected",
                    _ => "none"
                };
                response.Message = response.Status switch
                {
                    "pending" => "Follow request is pending approval",
                    "following" => "You are following this publisher",
                    "ended" => "Your follow has ended",
                    "rejected" => "Follow request was rejected",
                    _ => string.Empty
                };
                return Ok(response);
            }
        }

        var subscription = await subs.GetSubscriptionIncludingEndedAsync(accountId, publisher.Id);
        if (subscription != null)
        {
            response.Subscription = subscription;
            response.IsActive = subscription.IsActive;
            response.Notify = subscription.Notify;
            response.Status = subscription.IsActive ? "subscribed" : "ended";
            response.Message = subscription.IsActive
                ? "You are subscribed to this publisher"
                : "Your subscription has ended";
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
        [FromBody] SubscribeRequest? request = null)
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
                await subs.CancelSubscriptionAsync(accountId, publisher.Id);
                return Ok(new SubscriptionStatusResponse
                {
                    Status = "none",
                    Message = "Follow request cancelled",
                    RequiresApproval = true,
                    FollowRequest = null,
                    IsActive = false,
                    IsPending = false
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
                    Message = "Subscription cancelled successfully",
                    IsActive = false
                });

            return NotFound(new SubscriptionStatusResponse
            {
                Status = "none",
                Message = "No subscription found"
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

    public class SubscriberResponse
    {
        public SnPublisherSubscription Subscription { get; set; } = null!;
        public SnAccount? Account { get; set; }
    }

    public class UpdateNotifyRequest
    {
        public bool Notify { get; set; }
    }

    [HttpGet("{name}/subscribers")]
    [Authorize]
    public async Task<ActionResult<List<SubscriberResponse>>> ListSubscribers(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return Forbid();

        var query = db.PublisherSubscriptions
            .Where(s => s.PublisherId == publisher.Id)
            .Where(s => s.EndedAt == null);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var subscriptions = await query.OrderBy(s => s.CreatedAt).Skip(offset).Take(take).ToListAsync();

        var accountIds = subscriptions.Select(s => s.AccountId).Distinct().ToList();
        var accountDict = new Dictionary<Guid, SnAccount>();
        foreach (var id in accountIds)
        {
            var account = await accounts.TryGetAccount(id);
            if (account != null)
                accountDict[id] = SnAccount.FromProtoValue(account);
        }

        var result = subscriptions.Select(s => new SubscriberResponse
        {
            Subscription = s,
            Account = accountDict.GetValueOrDefault(s.AccountId)
        }).ToList();

        return Ok(result);
    }

    [HttpPost("{name}/subscribers/{accountId}")]
    [Authorize]
    public async Task<ActionResult<SubscriberResponse>> AddSubscriber(string name, Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var managerAccountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, managerAccountId, PublisherMemberRole.Manager))
            return Forbid();

        try
        {
            var subscription = await subs.AddSubscriberAsync(accountId, publisher.Id);
            var account = await accounts.GetAccount(accountId);
            return Ok(new SubscriberResponse
            {
                Subscription = subscription,
                Account = account != null ? SnAccount.FromProtoValue(account) : null
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{name}/subscribers/{accountId}")]
    [Authorize]
    public async Task<ActionResult> RemoveSubscriber(string name, Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var managerAccountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(publisher.Id, managerAccountId, PublisherMemberRole.Manager))
            return Forbid();

        var success = await subs.RemoveSubscriberAsync(accountId, publisher.Id, managerAccountId);
        if (!success)
            return NotFound("Subscriber not found");

        return NoContent();
    }

    [HttpPatch("{name}/subscribers/{accountId}/notify")]
    [Authorize]
    public async Task<ActionResult<SnPublisherSubscription>> UpdateSubscriberNotify(
        string name,
        Guid accountId,
        [FromBody] UpdateNotifyRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var subscriberAccountId = Guid.Parse(currentUser.Id);
        if (subscriberAccountId != accountId)
        {
            if (!await pub.IsMemberWithRole(publisher.Id, subscriberAccountId, PublisherMemberRole.Manager))
                return Forbid();
        }

        var subscription = await subs.UpdateSubscriberNotifyAsync(accountId, publisher.Id, request.Notify);
        if (subscription == null)
            return NotFound("Subscription not found");

        return Ok(subscription);
    }

    [HttpPatch("{name}/subscription/me/notify")]
    [Authorize]
    public async Task<ActionResult<SnPublisherSubscription>> UpdateMySubscriptionNotify(
        string name,
        [FromBody] UpdateNotifyRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == name);
        if (publisher == null)
            return NotFound("Publisher not found");

        var accountId = Guid.Parse(currentUser.Id);

        var subscription = await subs.UpdateSubscriberNotifyAsync(accountId, publisher.Id, request.Notify);
        if (subscription == null)
            return NotFound("Subscription not found");

        return Ok(subscription);
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

        var accountIds = requests.Select(r => r.AccountId).Distinct().ToList();
        var accountDict = new Dictionary<Guid, DyAccount>();
        foreach (var id in accountIds)
        {
            var account = await accounts.TryGetAccount(id);
            if (account != null)
                accountDict[id] = account;
        }

        foreach (var request in requests)
        {
            if (accountDict.TryGetValue(request.AccountId, out var requesterAccount))
                request.Account = SnAccount.FromProtoValue(requesterAccount);
        }

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

            var requesterAccount = await accounts.TryGetAccount(request.AccountId);
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

            var requesterAccount = await accounts.TryGetAccount(request.AccountId);
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
