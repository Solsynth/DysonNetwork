using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Swashbuckle.AspNetCore.Annotations;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    RemoteAccountService remoteAccountsHelper,
    DyAccountService.DyAccountServiceClient accounts,
    RemoteRealmService rs
) : ControllerBase
{
    [HttpGet("featured")]
    public async Task<ActionResult<List<SnPost>>> ListFeaturedPosts()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        var posts = await ps.ListFeaturedPostsAsync(currentUser);
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
        [FromQuery] bool showFediverse = false
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
        var userRealms = currentUser is null ? new List<Guid>() : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher =
            pubName == null
                ? null
                : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
        var realm = realmName == null ? null : (await rs.GetRealmBySlug(realmName));

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
        else
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

        query = query.FilterWithVisibility(
            currentUser,
            userFriends,
            userPublishers,
            isListing: true
        );

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
            // Prevent to load nested replied post
            if (post.RepliedPost != null)
                post.RepliedPost.RepliedPost = null;
        }
        

        posts = await ps.LoadPostInfo(posts, currentUser, true);

        // Load realm data for posts that have realm
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
        post = await ps.LoadPostInfo(post, currentUser);
        if (post.RealmId != null)
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());

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
        post = await ps.LoadPostInfo(post, currentUser);
        if (post.RealmId != null)
        {
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());
        }

        return Ok(post);
    }

    [HttpGet("{id:guid}/reactions")]
    public async Task<ActionResult<List<SnPostReaction>>> GetReactions(
        Guid id,
        [FromQuery] string? symbol = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var query = db.PostReactions.Where(e => e.PostId == id);
        if (symbol is not null)
            query = query.Where(e => e.Symbol == symbol);

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var reactions = await query
            .Include(r => r.Actor)
            .ThenInclude(r => r.Instance)
            .OrderBy(r => r.Symbol)
            .ThenByDescending(r => r.CreatedAt)
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

        var now = SystemClock.Instance.GetCurrentInstant();
        var post = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .OrderByDescending(p =>
                p.Upvotes * 2 - p.Downvotes + ((p.CreatedAt - now).TotalMinutes < 60 ? 5 : 0)
            )
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
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

        var posts = await db.Posts
            .Where(e =>
                e.RepliedPostId == id && e.PinMode == Shared.Models.PostPinMode.ReplyPage
            )
            .OrderByDescending(p => p.CreatedAt)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
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

        var totalCount = await db
            .Posts.Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .CountAsync();
        var posts = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
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
