using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.WebReader;
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
    AccountService.AccountServiceClient accounts,
    ActionLogService.ActionLogServiceClient als,
    PaymentService.PaymentServiceClient payments,
    PollService polls,
    RemoteRealmService rs
) : ControllerBase
{
    [HttpGet("featured")]
    public async Task<ActionResult<List<SnPost>>> ListFeaturedPosts()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;

        var posts = await ps.ListFeaturedPostsAsync(currentUser);
        return Ok(posts);
    }

    /// <summary>
    /// Retrieves a paginated list of posts with optional filtering and sorting.
    /// </summary>
    /// <param name="includeReplies">Whether to include reply posts in the results. If false, only root posts are returned.</param>
    /// <param name="offset">The number of posts to skip for pagination.</param>
    /// <param name="take">The maximum number of posts to return (default: 20).</param>
    /// <param name="pubName">Filter posts by publisher name.</param>
    /// <param name="realmName">Filter posts by realm slug.</param>
    /// <param name="type">Filter posts by post type (as integer).</param>
    /// <param name="categories">Filter posts by category slugs.</param>
    /// <param name="tags">Filter posts by tag slugs.</param>
    /// <param name="queryTerm">Search term to filter posts by title, description, or content.</param>
    /// <param name="queryVector">If true, uses vector search with the query term. If false, performs a simple ILIKE search.</param>
    /// <param name="onlyMedia">If true, only returns posts that have attachments.</param>
    /// <param name="shuffle">If true, returns posts in random order. If false, orders by published/created date (newest first).</param>
    /// <param name="pinned">If true, returns posts that pinned. If false, returns posts that are not pinned. If null, returns all posts.</param>
    /// <returns>
    /// Returns an ActionResult containing a list of Post objects that match the specified criteria.
    /// Includes an X-Total header with the total count of matching posts before pagination.
    /// </returns>
    /// <response code="200">Returns the list of posts matching the criteria.</response>
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
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;

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
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
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
            // Prevent load nested replied post
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
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db
            .Posts.Include(e => e.Publisher)
            .Where(e => e.Slug == slug && e.Publisher.Name == publisherName)
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
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
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
            .OrderBy(r => r.Symbol)
            .ThenByDescending(r => r.CreatedAt)
            .Take(take)
            .Skip(offset)
            .ToListAsync();

        var accountsProto = await remoteAccountsHelper.GetAccountBatch(
            reactions.Select(r => r.AccountId).ToList()
        );
        var accounts = accountsProto.ToDictionary(
            a => Guid.Parse(a.Id),
            a => SnAccount.FromProtoValue(a)
        );

        foreach (var reaction in reactions)
            if (accounts.TryGetValue(reaction.AccountId, out var account))
                reaction.Account = account;

        return Ok(reactions);
    }

    [HttpGet("{id:guid}/replies/featured")]
    public async Task<ActionResult<SnPost>> GetFeaturedReply(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
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
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var now = SystemClock.Instance.GetCurrentInstant();
        var posts = await db
            .Posts.Where(e =>
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
        var currentUser = currentUserValue as Account;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
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
        {
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();
        }

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    public class PostRequest
    {
        [MaxLength(1024)] public string? Title { get; set; }

        [MaxLength(4096)] public string? Description { get; set; }

        [MaxLength(1024)] public string? Slug { get; set; }
        public string? Content { get; set; }

        public Shared.Models.PostVisibility? Visibility { get; set; } =
            Shared.Models.PostVisibility.Public;

        public Shared.Models.PostType? Type { get; set; }
        public Shared.Models.PostEmbedView? EmbedView { get; set; }

        [MaxLength(16)] public List<string>? Tags { get; set; }

        [MaxLength(8)] public List<string>? Categories { get; set; }

        [MaxLength(32)] public List<string>? Attachments { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Instant? PublishedAt { get; set; }
        public Guid? RepliedPostId { get; set; }
        public Guid? ForwardedPostId { get; set; }
        public Guid? RealmId { get; set; }

        public Guid? PollId { get; set; }
    }

    [HttpPost]
    [RequiredPermission("global", "posts.create")]
    public async Task<ActionResult<SnPost>> CreatePost(
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        Shared.Models.SnPublisher? publisher;
        if (pubName is null)
        {
            // Use the first personal publisher
            publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                e.AccountId == accountId && e.Type == Shared.Models.PublisherType.Individual
            );
        }
        else
        {
            publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return BadRequest("Publisher was not found.");
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You need at least be an editor to post as this publisher.");
        }

        if (publisher is null)
            return BadRequest("Publisher was not found.");

        var post = new SnPost
        {
            Title = request.Title,
            Description = request.Description,
            Slug = request.Slug,
            Content = request.Content,
            Visibility = request.Visibility ?? Shared.Models.PostVisibility.Public,
            PublishedAt = request.PublishedAt,
            Type = request.Type ?? Shared.Models.PostType.Moment,
            Meta = request.Meta,
            EmbedView = request.EmbedView,
            Publisher = publisher,
        };

        if (request.RepliedPostId is not null)
        {
            var repliedPost = await db
                .Posts.Where(p => p.Id == request.RepliedPostId.Value)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            if (repliedPost is null)
                return BadRequest("Post replying to was not found.");
            post.RepliedPost = repliedPost;
            post.RepliedPostId = repliedPost.Id;
        }

        if (request.ForwardedPostId is not null)
        {
            var forwardedPost = await db
                .Posts.Where(p => p.Id == request.ForwardedPostId.Value)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            if (forwardedPost is null)
                return BadRequest("Forwarded post was not found.");
            post.ForwardedPost = forwardedPost;
            post.ForwardedPostId = forwardedPost.Id;
        }

        if (request.RealmId is not null)
        {
            var realm = await rs.GetRealm(request.RealmId.Value.ToString());
            if (
                !await rs.IsMemberWithRole(
                    realm.Id,
                    accountId,
                    [RealmMemberRole.Normal]
                )
            )
                return StatusCode(403, "You are not a member of this realm.");
            post.RealmId = realm.Id;
        }

        if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Meta ??= new Dictionary<string, object>();
                if (
                    !post.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Meta["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Meta["embeds"];
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                post.Meta["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        try
        {
            post = await ps.PostAsync(
                post,
                attachments: request.Attachments,
                tags: request.Tags,
                categories: request.Categories
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostCreate,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        post.Publisher = publisher;

        return post;
    }

    public class PostReactionRequest
    {
        [MaxLength(256)] public string Symbol { get; set; } = null!;
        public Shared.Models.PostReactionAttitude Attitude { get; set; }
    }

    public static readonly List<string> ReactionsAllowedDefault =
    [
        "thumb_up",
        "thumb_down",
        "just_okay",
        "cry",
        "confuse",
        "clap",
        "laugh",
        "angry",
        "party",
        "pray",
        "heart",
    ];

    [HttpPost("{id:guid}/reactions")]
    [Authorize]
    [RequiredPermission("global", "posts.react")]
    public async Task<ActionResult<SnPostReaction>> ReactPost(
        Guid id,
        [FromBody] PostReactionRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var friendsResponse = await accounts.ListFriendsAsync(
            new ListRelationshipSimpleRequest { AccountId = currentUser.Id.ToString() }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        if (!ReactionsAllowedDefault.Contains(request.Symbol))
            if (currentUser.PerkSubscription is null)
                return BadRequest("You need subscription to send custom reactions");

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var isSelfReact =
            post.Publisher.AccountId is not null && post.Publisher.AccountId == accountId;

        var isExistingReaction = await db.PostReactions.AnyAsync(r =>
            r.PostId == post.Id && r.Symbol == request.Symbol && r.AccountId == accountId
        );
        var reaction = new SnPostReaction
        {
            Symbol = request.Symbol,
            Attitude = request.Attitude,
            PostId = post.Id,
            AccountId = accountId,
        };
        var isRemoving = await ps.ModifyPostVotes(
            post,
            reaction,
            currentUser,
            isExistingReaction,
            isSelfReact
        );

        if (isRemoving)
            return NoContent();

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostReact,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                    { "reaction", Google.Protobuf.WellKnownTypes.Value.ForString(request.Symbol) },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(reaction);
    }

    public class PostAwardRequest
    {
        public decimal Amount { get; set; }
        public Shared.Models.PostReactionAttitude Attitude { get; set; }

        [MaxLength(4096)] public string? Message { get; set; }
    }

    [HttpGet("{id:guid}/awards")]
    public async Task<ActionResult<SnPostAward>> GetPostAwards(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var queryable = db.PostAwards.Where(a => a.PostId == id).AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var awards = await queryable.Take(take).Skip(offset).ToListAsync();

        return Ok(awards);
    }

    public class PostAwardResponse
    {
        public Guid OrderId { get; set; }
    }

    [HttpPost("{id:guid}/awards")]
    [Authorize]
    public async Task<ActionResult<PostAwardResponse>> AwardPost(
        Guid id,
        [FromBody] PostAwardRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        if (request.Attitude == Shared.Models.PostReactionAttitude.Neutral)
            return BadRequest("You cannot create a neutral post award");

        var friendsResponse = await accounts.ListFriendsAsync(
            new ListRelationshipSimpleRequest { AccountId = currentUser.Id.ToString() }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        var orderRemark = string.IsNullOrWhiteSpace(post.Title)
            ? "from @" + post.Publisher.Name
            : post.Title;
        var order = await payments.CreateOrderAsync(
            new CreateOrderRequest
            {
                ProductIdentifier = "posts.award",
                Currency = "points", // NSP - Source Points
                Remarks = $"Award post {orderRemark}",
                Amount = request.Amount.ToString(CultureInfo.InvariantCulture),
                Meta = GrpcTypeHelper.ConvertObjectToByteString(
                    new Dictionary<string, object?>
                    {
                        ["account_id"] = accountId,
                        ["post_id"] = post.Id,
                        ["amount"] = request.Amount.ToString(CultureInfo.InvariantCulture),
                        ["message"] = request.Message,
                        ["attitude"] = request.Attitude,
                    }
                ),
            }
        );

        return Ok(new PostAwardResponse() { OrderId = Guid.Parse(order.Id) });
    }

    public class PostPinRequest
    {
        [Required] public Shared.Models.PostPinMode Mode { get; set; }
    }

    [HttpPost("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> PinPost(Guid id, [FromBody] PostPinRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.RepliedPost)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.PublisherId, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You are not an editor of this publisher");

        if (request.Mode == Shared.Models.PostPinMode.RealmPage && post.RealmId != null)
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, "You are not a moderator of this realm");
        }

        try
        {
            await ps.PinPostAsync(post, currentUser, request.Mode);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostPin,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                    {
                        "mode",
                        Google.Protobuf.WellKnownTypes.Value.ForString(request.Mode.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> UnpinPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.RepliedPost)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.PublisherId, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You are not an editor of this publisher");

        if (post is { PinMode: Shared.Models.PostPinMode.RealmPage, RealmId: not null })
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, "You are not a moderator of this realm");
        }

        try
        {
            await ps.UnpinPostAsync(post, currentUser);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostUnpin,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(post);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<SnPost>> UpdatePost(
        Guid id,
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.Publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to edit this publisher's post.");

        if (pubName is not null)
        {
            var publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return NotFound();
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(
                    403,
                    "You need at least be an editor to transfer this post to this publisher."
                );
            post.PublisherId = publisher.Id;
            post.Publisher = publisher;
        }

        if (request.Title is not null)
            post.Title = request.Title;
        if (request.Description is not null)
            post.Description = request.Description;
        if (request.Slug is not null)
            post.Slug = request.Slug;
        if (request.Content is not null)
            post.Content = request.Content;
        if (request.Visibility is not null)
            post.Visibility = request.Visibility.Value;
        if (request.Type is not null)
            post.Type = request.Type.Value;
        if (request.Meta is not null)
            post.Meta = request.Meta;

        // The same, this field can be null, so update it anyway.
        post.EmbedView = request.EmbedView;

        // All the fields are updated when the request contains the specific fields
        // But the Poll can be null, so it will be updated whatever it included in requests or not
        if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Meta ??= new Dictionary<string, object>();
                if (
                    !post.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Meta["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Meta["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "poll"
                );
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                post.Meta["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        else
        {
            post.Meta ??= new Dictionary<string, object>();
            if (
                !post.Meta.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Meta["embeds"];
            // Remove all old poll embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
        }

        // The realm is the same as well as the poll
        if (request.RealmId is not null)
        {
            var realm = await rs.GetRealm(request.RealmId.Value.ToString());
            if (
                !await rs.IsMemberWithRole(
                    realm.Id,
                    accountId,
                    new List<int> { RealmMemberRole.Normal }
                )
            )
                return StatusCode(403, "You are not a member of this realm.");
            post.RealmId = realm.Id;
        }
        else
        {
            post.RealmId = null;
        }

        try
        {
            post = await ps.UpdatePostAsync(
                post,
                attachments: request.Attachments,
                tags: request.Tags,
                categories: request.Categories,
                publishedAt: request.PublishedAt
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostUpdate,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<SnPost>> DeletePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (
            !await pub.IsMemberWithRole(
                post.Publisher.Id,
                Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor
            )
        )
            return StatusCode(
                403,
                "You need at least be an editor to delete the publisher's post."
            );

        await ps.DeletePostAsync(post);

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostDelete,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return NoContent();
    }
}