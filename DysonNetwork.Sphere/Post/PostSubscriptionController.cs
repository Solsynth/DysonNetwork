using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
[ApiFeature("posts.subscriptions", Revision = 1)]
public class PostSubscriptionController(
    AppDatabase db,
    PublisherService pub,
    PostService ps,
    PostCollectionService pcs,
    DyProfileService.DyProfileServiceClient accounts
) : ControllerBase
{
    public class PostSubscriptionRequest
    {
        public bool? Reactions { get; set; }
        public bool? Forwards { get; set; }
        public bool? Edits { get; set; }
    }

    public class PostSubscriptionListItem
    {
        public required SnPostSubscription Subscription { get; set; }
        public required SnPost Post { get; set; }
    }

    [HttpPost("{id:guid}/subscribe")]
    [AskPermission(PermissionKeys.PostSubscriptionsManage)]
    [Authorize]
    public async Task<ActionResult<SnPostSubscription>> SubscribePost(
        Guid id,
        [FromBody] PostSubscriptionRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var post = await GetVisiblePostAsync(id, currentUser);
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var existingSubscription = await db.PostSubscriptions
            .FirstOrDefaultAsync(s => s.PostId == id && s.AccountId == accountId);

        if (existingSubscription is not null)
        {
            ApplySubscriptionRequest(existingSubscription, request);
            await db.SaveChangesAsync();
            return Ok(existingSubscription);
        }

        var subscription = new SnPostSubscription
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            PostId = id,
        };
        ApplySubscriptionRequest(subscription, request);

        db.PostSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPostSubscription), new { id }, subscription);
    }

    [HttpPost("{id:guid}/unsubscribe")]
    [AskPermission(PermissionKeys.PostSubscriptionsManage)]
    [Authorize]
    public async Task<IActionResult> UnsubscribePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var accountId = Guid.Parse(currentUser.Id);
        var subscription = await db.PostSubscriptions
            .FirstOrDefaultAsync(s => s.PostId == id && s.AccountId == accountId);

        if (subscription is null)
            return NoContent();

        db.PostSubscriptions.Remove(subscription);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnPostSubscription>> GetPostSubscription(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var post = await GetVisiblePostAsync(id, currentUser);
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var subscription = await db.PostSubscriptions
            .FirstOrDefaultAsync(s => s.PostId == id && s.AccountId == accountId);

        if (subscription is null)
            return NotFound(new ApiError { Code = "POST_SUBSCRIPTION_NOT_FOUND", Message = "Subscription not found.", Status = 404 });

        return Ok(subscription);
    }

    [HttpGet("subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<PostSubscriptionListItem>>> ListPostSubscriptions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var accountId = Guid.Parse(currentUser.Id);
        var subscriptionsQuery = db.PostSubscriptions
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(s => s.CreatedAt);

        var totalCount = await subscriptionsQuery.CountAsync();
        var subscriptions = await subscriptionsQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var postIds = subscriptions.Select(s => s.PostId).ToList();
        var posts = await db.Posts
            .Where(p => postIds.Contains(p.Id))
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.FeaturedRecords)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(posts);

        var postMap = posts.ToDictionary(p => p.Id);
        var result = subscriptions
            .Where(s => postMap.ContainsKey(s.PostId))
            .Select(s => new PostSubscriptionListItem
            {
                Subscription = s,
                Post = postMap[s.PostId],
            })
            .ToList();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(result);
    }

    private async Task<SnPost?> GetVisiblePostAsync(Guid id, DyAccount currentUser)
    {
        var userFriends = (await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        )).AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        return await db.Posts
            .Where(p => p.Id == id)
            .Include(p => p.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
    }

    private static void ApplySubscriptionRequest(
        SnPostSubscription subscription,
        PostSubscriptionRequest? request
    )
    {
        if (request is null)
            return;

        if (request.Reactions.HasValue)
            subscription.NotifyReactions = request.Reactions.Value;
        if (request.Forwards.HasValue)
            subscription.NotifyForwards = request.Forwards.Value;
        if (request.Edits.HasValue)
            subscription.NotifyEdits = request.Edits.Value;
    }
}
