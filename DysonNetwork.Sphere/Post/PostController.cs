using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Swashbuckle.AspNetCore.Annotations;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    RemoteAccountService remoteAccountsHelper,
    DyProfileService.DyProfileServiceClient accounts,
    RemoteRealmService rs
) : ControllerBase
{
    private async Task<(HashSet<Guid>? gatekeptPublisherIds, HashSet<Guid>? subscriberPublisherIds)> GetGatekeepInfoAsync(
        IQueryable<Guid> publisherIdsInQuery,
        DyAccount? currentUser)
    {
        var publisherIds = await publisherIdsInQuery.Distinct().ToListAsync();
        if (publisherIds.Count == 0)
            return (null, null);

        var gatekeptPublisherIds = (await db.Publishers
            .Where(p => publisherIds.Contains(p.Id) && p.GatekeptFollows == true)
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        HashSet<Guid>? subscriberPublisherIds = null;
        if (gatekeptPublisherIds.Count > 0)
        {
            if (currentUser != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var activeSubscriptions = await db.PublisherSubscriptions
                    .Where(s => s.AccountId == currentAccountId && s.EndedAt == null && publisherIds.Contains(s.PublisherId))
                    .Select(s => s.PublisherId)
                    .ToListAsync();
                subscriberPublisherIds = activeSubscriptions.ToHashSet();
            }
            else
            {
                subscriberPublisherIds = [];
            }
        }

        return (gatekeptPublisherIds.Count > 0 ? gatekeptPublisherIds : null, subscriberPublisherIds);
    }

    public class ThreadedReplyNode
    {
        public required SnPost Post { get; set; }
        public required int Depth { get; set; }
        public required Guid? ParentId { get; set; }
    }

    private static void FlattenThreadedReplies(
        SnPost post,
        Dictionary<Guid, List<SnPost>> repliesByParent,
        int depth,
        List<ThreadedReplyNode> result
    )
    {
        post.RepliedPost = null;
        post.ForwardedPost = null;
        result.Add(new ThreadedReplyNode { Post = post, Depth = depth, ParentId = post.RepliedPostId });

        var replies = repliesByParent.GetValueOrDefault(post.Id, []);
        foreach (var reply in replies)
            FlattenThreadedReplies(reply, repliesByParent, depth + 1, result);
    }

    [HttpGet("featured")]
    public async Task<ActionResult<List<SnPost>>> ListFeaturedPosts()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        var posts = await ps.ListFeaturedPostsAsync(currentUser);
        return Ok(posts);
    }

    [HttpGet("drafts")]
    [Authorize]
    public async Task<ActionResult<List<SnPost>>> ListDrafts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var userPublishers = await pub.GetUserPublishers(accountId);
        var publisherIds = userPublishers.Select(p => p.Id).ToList();

        if (pubName is not null)
        {
            var selectedPublisher = await pub.GetPublisherByName(pubName);
            if (selectedPublisher is null)
                return NotFound();
            if (
                !await pub.IsMemberWithRole(
                    selectedPublisher.Id,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                return StatusCode(403, "You need at least be an editor to view drafts.");

            publisherIds = [selectedPublisher.Id];
        }

        var query = db
            .Posts.Where(p =>
                p.DraftedAt != null && p.PublisherId.HasValue && publisherIds.Contains(p.PublisherId.Value)
            )
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .OrderByDescending(e => e.DraftedAt ?? e.UpdatedAt);

        var totalCount = await query.CountAsync();
        var posts = await query.Skip(offset).Take(take).ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(posts);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SnPost>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [SwaggerOperation(
        Summary = "Retrieves a paginated list of posts",
        Description =
            "Gets posts with various filtering and sorting options. Supports pagination and advanced search capabilities.",
        OperationId = "ListPosts",
        Tags = ["Posts"]
    )]
    [SwaggerResponse(
        StatusCodes.Status200OK,
        "Successfully retrieved the list of posts",
        typeof(List<SnPost>)
    )]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request parameters")]
    public async Task<ActionResult<List<SnPost>>> ListPosts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "shuffle")] bool shuffle = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "order")] string? order = null,
        [FromQuery(Name = "orderDesc")] bool orderDesc = true,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null,
        [FromQuery] bool showFediverse = false,
        [FromQuery(Name = "mentioned")] string? mentioned = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher =
            pubName == null
                ? null
                : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);

        var query = db
            .Posts.Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .AsQueryable();
        if (publisher != null)
            query = query.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            query = query.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            query = query.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            query = query.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            query = query.Where(e => e.Attachments.Count > 0);
        if (!showFediverse)
            query = query.Where(p => p.FediverseUri == null);

        if (realm != null)
            query = query.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            query = query.Where(p =>
                p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value)
            );

        if (periodStart != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest(
                    "You need pass extra realm or publisher params in order to filter with pinned posts."
                );
            case false:
                query = query.Where(p => p.PinMode == null);
                break;
        }

        query = includeReplies switch
        {
            false => query.Where(e => e.RepliedPostId == null),
            true => query.Where(e => e.RepliedPostId != null),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(queryTerm))
        {
            query = query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{queryTerm}%"))
                || (p.Description != null && EF.Functions.ILike(p.Description, $"%{queryTerm}%"))
                || (p.Content != null && EF.Functions.ILike(p.Content, $"%{queryTerm}%"))
            );
        }

        if (!string.IsNullOrWhiteSpace(mentioned))
        {
            var normalizedMentioned = mentioned.ToLowerInvariant();
            query = query.Where(p =>
                p.Content != null && (
                    EF.Functions.ILike(p.Content, $"%@{mentioned}%") ||
                    p.Mentions != null && p.Mentions.Any(m => m.Username != null && EF.Functions.ILike(m.Username, normalizedMentioned))
                )
            );
        }

        var publisherIdsInQuery = publisher != null
            ? new List<Guid> { publisher.Id }
            : await query.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value).Distinct().ToListAsync();

        HashSet<Guid>? gatekeptPublisherIds = null;
        HashSet<Guid>? subscriberPublisherIds = null;
        HashSet<Guid>? shadowbannedPublisherIds = null;

        if (publisherIdsInQuery.Count > 0)
        {
            gatekeptPublisherIds = (await db.Publishers
                .Where(p => publisherIdsInQuery.Contains(p.Id) && p.GatekeptFollows == true)
                .Select(p => p.Id)
                .ToListAsync()).ToHashSet();

            shadowbannedPublisherIds = (await db.Publishers
                .Where(p => publisherIdsInQuery.Contains(p.Id) && p.ShadowbanReason != null && p.ShadowbanReason != PublisherShadowbanReason.None)
                .Select(p => p.Id)
                .ToListAsync()).ToHashSet();

            if (gatekeptPublisherIds.Count > 0)
            {
                if (currentUser != null)
                {
                    var currentAccountId = Guid.Parse(currentUser.Id);
                    var activeSubscriptions = await db.PublisherSubscriptions
                        .Where(s => s.AccountId == currentAccountId && s.EndedAt == null && publisherIdsInQuery.Contains(s.PublisherId))
                        .Select(s => s.PublisherId)
                        .ToListAsync();
                    subscriberPublisherIds = activeSubscriptions.ToHashSet();
                }
                else
                {
                    subscriberPublisherIds = [];
                }
            }
        }

        query = query.FilterWithVisibility(
            currentUser,
            userFriends,
            userPublishers,
            isListing: true,
            gatekeptPublisherIds,
            subscriberPublisherIds
        );

        if (shadowbannedPublisherIds != null && shadowbannedPublisherIds.Count > 0)
        {
            query = query.Where(p =>
                !shadowbannedPublisherIds.Contains(p.PublisherId!.Value) &&
                (p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None));
        }

        if (shuffle)
        {
            query = query.OrderBy(e => EF.Functions.Random());
        }
        else
        {
            query = order switch
            {
                "popularity" => orderDesc
                    ? query.OrderByDescending(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore)
                    : query.OrderBy(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore),
                _ => orderDesc
                    ? query.OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
                    : query.OrderBy(e => e.PublishedAt ?? e.CreatedAt)
            };
        }

        var totalCount = await query.CountAsync();

        var posts = await query.Skip(offset).Take(take).ToListAsync();
        foreach (var post in posts)
        {
            if (post.RepliedPost != null)
                post.RepliedPost.RepliedPost = null;
        }
        

        posts = await ps.LoadPostInfo(posts, currentUser, true);

        await LoadPostsRealmsAsync(posts, rs);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    private static async Task LoadPostsRealmsAsync(List<SnPost> posts, RemoteRealmService rs)
    {
        var postRealmIds = posts
            .Where(p => p.RealmId != null)
            .Select(p => p.RealmId!.Value)
            .Distinct()
            .ToList();
        if (!postRealmIds.Any())
            return;

        var realms = await rs.GetRealmBatch(postRealmIds.Select(id => id.ToString()).ToList());
        var realmDict = realms.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.FirstOrDefault());

        foreach (var post in posts.Where(p => p.RealmId != null))
        {
            if (realmDict.TryGetValue(post.RealmId!.Value, out var realm))
            {
                post.Realm = realm;
            }
        }
    }

    [HttpGet("{publisherName}/{slug}")]
    public async Task<ActionResult<SnPost>> GetPost(string publisherName, string slug)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db.Posts
            .Include(e => e.Publisher)
            .Where(e => e.Slug == slug && e.Publisher != null && e.Publisher.Name == publisherName)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.PublisherId.HasValue && post.Publisher?.GatekeptFollows == true)
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == post.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == post.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        post = await ps.LoadPostInfo(post, currentUser);
        if (post.RealmId != null)
        {
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());
        }

        if (currentUser != null)
            await ps.IncreaseViewCount(post.Id, currentUser.Id, isDetailView: true);
        else
            await ps.IncreaseViewCount(post.Id, isDetailView: true);

        return Ok(post);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnPost>> GetPost(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.PublisherId.HasValue && post.Publisher?.GatekeptFollows == true)
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == post.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == post.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        post = await ps.LoadPostInfo(post, currentUser);
        if (post.RealmId != null)
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());

        if (currentUser != null)
            await ps.IncreaseViewCount(post.Id, currentUser.Id, isDetailView: true);
        else
            await ps.IncreaseViewCount(post.Id, isDetailView: true);

        return Ok(post);
    }

    [HttpGet("{id:guid}/prev")]
    public async Task<ActionResult<SnPost>> GetPrevPost(
        Guid id,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null,
        [FromQuery] bool showFediverse = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher = pubName == null
            ? null
            : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        var currentPost = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (currentPost is null)
            return NotFound("Current post not found");

        var currentTime = currentPost.PublishedAt ?? currentPost.CreatedAt;

        var query = db.Posts
            .Where(e => (e.PublishedAt ?? e.CreatedAt) < currentTime)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .AsQueryable();

        if (publisher != null)
            query = query.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            query = query.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            query = query.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            query = query.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            query = query.Where(e => e.Attachments.Count > 0);
        if (!showFediverse)
            query = query.Where(p => p.FediverseUri == null);

        if (realm != null)
            query = query.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            query = query.Where(p => p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value));

        if (periodStart != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest("You need pass extra realm or publisher params in order to filter with pinned posts.");
            case false:
                query = query.Where(p => p.PinMode == null);
                break;
        }

        query = includeReplies switch
        {
            false => query.Where(e => e.RepliedPostId == null),
            true => query.Where(e => e.RepliedPostId != null),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(queryTerm))
        {
            query = query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{queryTerm}%"))
                || (p.Description != null && EF.Functions.ILike(p.Description, $"%{queryTerm}%"))
                || (p.Content != null && EF.Functions.ILike(p.Content, $"%{queryTerm}%"))
            );
        }

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            query.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value),
            currentUser
        );

        query = query.FilterWithVisibility(
            currentUser,
            userFriends,
            userPublishers,
            isListing: true,
            gatekeptPublisherIds,
            subscriberPublisherIds
        );

        var prevPost = await query
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .FirstOrDefaultAsync();

        if (prevPost is null)
            return NotFound("No previous post found");

        if (prevPost.PublisherId.HasValue && prevPost.Publisher?.GatekeptFollows == true)
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == prevPost.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == prevPost.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        prevPost = await ps.LoadPostInfo(prevPost, currentUser);
        if (prevPost.RealmId != null)
            prevPost.Realm = await rs.GetRealm(prevPost.RealmId.Value.ToString());

        return Ok(prevPost);
    }

    [HttpGet("{id:guid}/next")]
    public async Task<ActionResult<SnPost>> GetNextPost(
        Guid id,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null,
        [FromQuery] bool showFediverse = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher = pubName == null
            ? null
            : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        var currentPost = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (currentPost is null)
            return NotFound("Current post not found");

        var currentTime = currentPost.PublishedAt ?? currentPost.CreatedAt;

        var query = db.Posts
            .Where(e => (e.PublishedAt ?? e.CreatedAt) > currentTime)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .AsQueryable();

        if (publisher != null)
            query = query.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            query = query.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            query = query.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            query = query.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            query = query.Where(e => e.Attachments.Count > 0);
        if (!showFediverse)
            query = query.Where(p => p.FediverseUri == null);

        if (realm != null)
            query = query.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            query = query.Where(p => p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value));

        if (periodStart != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest("You need pass extra realm or publisher params in order to filter with pinned posts.");
            case false:
                query = query.Where(p => p.PinMode == null);
                break;
        }

        query = includeReplies switch
        {
            false => query.Where(e => e.RepliedPostId == null),
            true => query.Where(e => e.RepliedPostId != null),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(queryTerm))
        {
            query = query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{queryTerm}%"))
                || (p.Description != null && EF.Functions.ILike(p.Description, $"%{queryTerm}%"))
                || (p.Content != null && EF.Functions.ILike(p.Content, $"%{queryTerm}%"))
            );
        }

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            query.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value),
            currentUser
        );

        query = query.FilterWithVisibility(
            currentUser,
            userFriends,
            userPublishers,
            isListing: true,
            gatekeptPublisherIds,
            subscriberPublisherIds
        );

        var nextPost = await query
            .OrderBy(e => e.PublishedAt ?? e.CreatedAt)
            .FirstOrDefaultAsync();

        if (nextPost is null)
            return NotFound("No next post found");

        if (nextPost.PublisherId.HasValue && nextPost.Publisher?.GatekeptFollows == true)
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == nextPost.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == nextPost.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        nextPost = await ps.LoadPostInfo(nextPost, currentUser);
        if (nextPost.RealmId != null)
            nextPost.Realm = await rs.GetRealm(nextPost.RealmId.Value.ToString());

        return Ok(nextPost);
    }

    [HttpGet("{id:guid}/reactions")]
    public async Task<ActionResult<List<SnPostReaction>>> GetReactions(
        Guid id,
        [FromQuery] string? symbol = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "order")] string? order = null
    )
    {
        var query = db.PostReactions.Where(e => e.PostId == id);
        if (symbol is not null)
            query = query.Where(e => e.Symbol == symbol);

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        query = order?.ToLowerInvariant() switch
        {
            "created" => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderBy(r => r.Symbol).ThenByDescending(r => r.CreatedAt)
        };

        var reactions = await query
            .Include(r => r.Actor)
            .ThenInclude(r => r.Instance)
            .Take(take)
            .Skip(offset)
            .ToListAsync();

        var accountsProto = await remoteAccountsHelper.GetAccountBatch(
            reactions.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).ToList()
        );
        var accountsData = accountsProto.ToDictionary(
            a => Guid.Parse(a.Id),
            SnAccount.FromProtoValue
        );

        foreach (var reaction in reactions)
            if (reaction.AccountId.HasValue && accountsData.TryGetValue(reaction.AccountId.Value, out var account))
                reaction.Account = account;

        return Ok(reactions);
    }

    [HttpGet("{id:guid}/replies/featured")]
    public async Task<ActionResult<SnPost>> GetFeaturedReply(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var now = SystemClock.Instance.GetCurrentInstant();
        var post = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .OrderByDescending(p =>
                p.Upvotes * 2 - p.Downvotes + ((p.CreatedAt - now).TotalMinutes < 60 ? 5 : 0)
            )
            .FilterWithVisibility(currentUser, userFriends, userPublishers, gatekeptPublisherIds: gatekeptPublisherIds, followerPublisherIds: subscriberPublisherIds)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();
        post = await ps.LoadPostInfo(post, currentUser, true);

        return Ok(post);
    }

    [HttpGet("{id:guid}/replies/pinned")]
    public async Task<ActionResult<List<SnPost>>> ListPinnedReplies(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var posts = await db.Posts
            .Where(e =>
                e.RepliedPostId == id && e.PinMode == Shared.Models.PostPinMode.ReplyPage
            )
            .OrderByDescending(p => p.CreatedAt)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, gatekeptPublisherIds: gatekeptPublisherIds, followerPublisherIds: subscriberPublisherIds)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser);

        return Ok(posts);
    }

    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<List<SnPost>>> ListReplies(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .CountAsync();
        var posts = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    [HttpGet("{id:guid}/replies/threaded")]
    public async Task<ActionResult<List<ThreadedReplyNode>>> ListThreadedReplies(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .CountAsync();

        var rootReplies = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .AsNoTracking()
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        rootReplies = await ps.LoadPostInfo(rootReplies, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();

        if (rootReplies.Count == 0)
            return Ok(new List<ThreadedReplyNode>());

        var repliesByParent = new Dictionary<Guid, List<SnPost>>();
        var visited = rootReplies.Select(e => e.Id).ToHashSet();
        var frontier = rootReplies.Select(e => e.Id).ToList();

        while (frontier.Count > 0)
        {
            var children = await db
                .Posts.Where(e => e.RepliedPostId != null && frontier.Contains(e.RepliedPostId.Value))
                .Include(e => e.ForwardedPost)
                .Include(e => e.Categories)
                .Include(e => e.Tags)
                .Include(e => e.FeaturedRecords)
                .AsNoTracking()
                .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
                .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
                .ToListAsync();

            children = children.Where(e => visited.Add(e.Id)).ToList();
            if (children.Count == 0)
                break;

            children = await ps.LoadPostInfo(children, currentUser, true);

            foreach (var child in children)
            {
                if (child.RepliedPostId is not { } parentId)
                    continue;

                if (!repliesByParent.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    repliesByParent[parentId] = siblings;
                }

                siblings.Add(child);
            }

            frontier = children.Select(e => e.Id).ToList();
        }

        var tree = new List<ThreadedReplyNode>();
        foreach (var root in rootReplies)
            FlattenThreadedReplies(root, repliesByParent, 0, tree);
        return Ok(tree);
    }

    [HttpGet("{id:guid}/forwards")]
    public async Task<ActionResult<List<SnPost>>> ListForwards(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.ForwardedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.ForwardedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .CountAsync();

        var posts = await db
            .Posts.Where(e => e.ForwardedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }
}
