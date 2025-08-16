using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Content;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.WebReader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    AccountService.AccountServiceClient accounts,
    ActionLogService.ActionLogServiceClient als,
    PollService polls,
    RealmService rs
)
    : ControllerBase
{
    [HttpGet("featured")]
    public async Task<ActionResult<List<Post>>> ListFeaturedPosts()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;

        var posts = await ps.ListFeaturedPostsAsync(currentUser);
        return Ok(posts);
    }

    [HttpGet]
    public async Task<ActionResult<List<Post>>> ListPosts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "vector")] bool queryVector = false,
        [FromQuery(Name = "replies")] bool includeReplies = false,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "shuffle")] bool shuffle = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var publisher = pubName == null ? null : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
        var realm = realmName == null ? null : await db.Realms.FirstOrDefaultAsync(r => r.Slug == realmName);

        var query = db.Posts
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .AsQueryable();
        if (publisher != null)
            query = query.Where(p => p.PublisherId == publisher.Id);
        if (realm != null)
            query = query.Where(p => p.RealmId == realm.Id);
        if (type != null)
            query = query.Where(p => p.Type == (PostType)type);
        if (categories is { Count: > 0 })
            query = query.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            query = query.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (!includeReplies)
            query = query.Where(e => e.RepliedPostId == null);
        if (onlyMedia)
            query = query.Where(e => e.Attachments.Count > 0);

        if (!string.IsNullOrWhiteSpace(queryTerm))
        {
            if (queryVector)
                query = query.Where(p => p.SearchVector.Matches(EF.Functions.ToTsQuery(queryTerm)));
            else
                query = query.Where(p =>
                    (p.Title != null && EF.Functions.ILike(p.Title, $"%{queryTerm}%")) ||
                    (p.Description != null && EF.Functions.ILike(p.Description, $"%{queryTerm}%")) ||
                    (p.Content != null && EF.Functions.ILike(p.Content, $"%{queryTerm}%"))
                );
        }

        query = query
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true);

        var totalCount = await query
            .CountAsync();

        if (shuffle)
            query = query.OrderBy(e => EF.Functions.Random());
        else
            query = query.OrderByDescending(e => e.PublishedAt ?? e.CreatedAt);

        var posts = await query
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    [HttpGet("{publisherName}/{slug}")]
    public async Task<ActionResult<Post>> GetPost(string publisherName, string slug)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db.Posts
            .Include(e => e.Publisher)
            .Where(e => e.Slug == slug && e.Publisher.Name == publisherName)
            .Include(e => e.Realm)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();
        post = await ps.LoadPostInfo(post, currentUser);

        // Track view - use the account ID as viewer ID if user is logged in
        await ps.IncreaseViewCount(post.Id, currentUser?.Id);

        return Ok(post);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Post>> GetPost(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Realm)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();
        post = await ps.LoadPostInfo(post, currentUser);

        // Track view - use the account ID as viewer ID if user is logged in
        await ps.IncreaseViewCount(post.Id, currentUser?.Id);

        return Ok(post);
    }

    [HttpGet("search")]
    [Obsolete("Use the new ListPost API")]
    public async Task<ActionResult<List<Post>>> SearchPosts(
        [FromQuery] string query,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool useVector = true
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query cannot be empty");

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var queryable = db.Posts
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .AsQueryable();
        if (useVector)
            queryable = queryable.Where(p => p.SearchVector.Matches(EF.Functions.ToTsQuery(query)));
        else
            queryable = queryable.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{query}%")) ||
                (p.Description != null && EF.Functions.ILike(p.Description, $"%{query}%")) ||
                (p.Content != null && EF.Functions.ILike(p.Content, $"%{query}%"))
            );

        var totalCount = await queryable.CountAsync();

        var posts = await queryable
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(posts);
    }

    [HttpGet("{id:guid}/reactions")]
    public async Task<ActionResult<List<PostReaction>>> GetReactions(
        Guid id,
        [FromQuery] string? symbol = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var query = db.PostReactions
            .Where(e => e.PostId == id);
        if (symbol is not null) query = query.Where(e => e.Symbol == symbol);

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var reactions = await query
            .OrderBy(r => r.Symbol)
            .ThenByDescending(r => r.CreatedAt)
            .Take(take)
            .Skip(offset)
            .ToListAsync();
        return Ok(reactions);
    }

    [HttpGet("{id:guid}/replies/featured")]
    public async Task<ActionResult<Post>> GetFeaturedReply(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var now = SystemClock.Instance.GetCurrentInstant();
        var post = await db.Posts
            .Where(e => e.RepliedPostId == id)
            .OrderByDescending(p =>
                p.Upvotes * 2 -
                p.Downvotes +
                ((p.CreatedAt - now).TotalMinutes < 60 ? 5 : 0)
            )
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();
        post = await ps.LoadPostInfo(post, currentUser);

        // Track view - use the account ID as viewer ID if user is logged in
        await ps.IncreaseViewCount(post.Id, currentUser?.Id);

        return await ps.LoadPostInfo(post);
    }

    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<List<Post>>> ListReplies(Guid id, [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (parent is null) return NotFound();

        var totalCount = await db.Posts
            .Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .CountAsync();
        var posts = await db.Posts
            .Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount =
                reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    public class PostRequest
    {
        [MaxLength(1024)] public string? Title { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        [MaxLength(1024)] public string? Slug { get; set; }
        public string? Content { get; set; }
        public PostVisibility? Visibility { get; set; } = PostVisibility.Public;
        public PostType? Type { get; set; }
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
    public async Task<ActionResult<Post>> CreatePost(
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        Publisher.Publisher? publisher;
        if (pubName is null)
        {
            // Use the first personal publisher
            publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                e.AccountId == accountId && e.Type == Publisher.PublisherType.Individual);
        }
        else
        {
            publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null) return BadRequest("Publisher was not found.");
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
                return StatusCode(403, "You need at least be an editor to post as this publisher.");
        }

        if (publisher is null) return BadRequest("Publisher was not found.");

        var post = new Post
        {
            Title = request.Title,
            Description = request.Description,
            Slug = request.Slug,
            Content = request.Content,
            Visibility = request.Visibility ?? PostVisibility.Public,
            PublishedAt = request.PublishedAt,
            Type = request.Type ?? PostType.Moment,
            Meta = request.Meta,
            Publisher = publisher,
        };

        if (request.RepliedPostId is not null)
        {
            var repliedPost = await db.Posts.FindAsync(request.RepliedPostId.Value);
            if (repliedPost is null) return BadRequest("Post replying to was not found.");
            post.RepliedPost = repliedPost;
            post.RepliedPostId = repliedPost.Id;
        }

        if (request.ForwardedPostId is not null)
        {
            var forwardedPost = await db.Posts.FindAsync(request.ForwardedPostId.Value);
            if (forwardedPost is null) return BadRequest("Forwarded post was not found.");
            post.ForwardedPost = forwardedPost;
            post.ForwardedPostId = forwardedPost.Id;
        }

        if (request.RealmId is not null)
        {
            var realm = await db.Realms.FindAsync(request.RealmId.Value);
            if (realm is null) return BadRequest("Realm was not found.");
            if (!await rs.IsMemberWithRole(realm.Id, accountId, RealmMemberRole.Normal))
                return StatusCode(403, "You are not a member of this realm.");
            post.RealmId = realm.Id;
        }

        if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Meta ??= new Dictionary<string, object>();
                if (!post.Meta.TryGetValue("embeds", out var existingEmbeds) ||
                    existingEmbeds is not List<EmbeddableBase>)
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

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.PostCreate,
            Meta = { { "post_id", Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString()) } },
            AccountId = currentUser.Id.ToString(),
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return post;
    }

    public class PostReactionRequest
    {
        [MaxLength(256)] public string Symbol { get; set; } = null!;
        public PostReactionAttitude Attitude { get; set; }
    }

    [HttpPost("{id:guid}/reactions")]
    [Authorize]
    [RequiredPermission("global", "posts.react")]
    public async Task<ActionResult<PostReaction>> ReactPost(Guid id, [FromBody] PostReactionRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var friendsResponse =
            await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id.ToString() });
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var isSelfReact = post.Publisher.AccountId is not null && post.Publisher.AccountId == accountId;

        var isExistingReaction = await db.PostReactions
            .AnyAsync(r => r.PostId == post.Id &&
                           r.Symbol == request.Symbol &&
                           r.AccountId == accountId);
        var reaction = new PostReaction
        {
            Symbol = request.Symbol,
            Attitude = request.Attitude,
            PostId = post.Id,
            AccountId = accountId
        };
        var isRemoving = await ps.ModifyPostVotes(
            post,
            reaction,
            currentUser,
            isExistingReaction,
            isSelfReact
        );

        if (isRemoving) return NoContent();

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.PostReact,
            Meta =
            {
                { "post_id", Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString()) },
                { "reaction", Google.Protobuf.WellKnownTypes.Value.ForString(request.Symbol) }
            },
            AccountId = currentUser.Id.ToString(),
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(reaction);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<Post>> UpdatePost(
        Guid id,
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.Publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to edit this publisher's post.");

        if (pubName is not null)
        {
            var publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null) return NotFound();
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
                return StatusCode(403, "You need at least be an editor to transfer this post to this publisher.");
            post.PublisherId = publisher.Id;
            post.Publisher = publisher;
        }

        if (request.Title is not null) post.Title = request.Title;
        if (request.Description is not null) post.Description = request.Description;
        if (request.Slug is not null) post.Slug = request.Slug;
        if (request.Content is not null) post.Content = request.Content;
        if (request.Visibility is not null) post.Visibility = request.Visibility.Value;
        if (request.Type is not null) post.Type = request.Type.Value;
        if (request.Meta is not null) post.Meta = request.Meta;

        // All the fields are updated when the request contains the specific fields
        // But the Poll can be null, so it will be updated whatever it included in requests or not
        if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Meta ??= new Dictionary<string, object>();
                if (!post.Meta.TryGetValue("embeds", out var existingEmbeds) ||
                    existingEmbeds is not List<EmbeddableBase>)
                    post.Meta["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Meta["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
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
            if (!post.Meta.TryGetValue("embeds", out var existingEmbeds) ||
                existingEmbeds is not List<EmbeddableBase>)
                post.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Meta["embeds"];
            // Remove all old poll embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
        }

        // The realm is the same as well as the poll
        if (request.RealmId is not null)
        {
            var realm = await db.Realms.FindAsync(request.RealmId.Value);
            if (realm is null) return BadRequest("Realm was not found.");
            if (!await rs.IsMemberWithRole(realm.Id, accountId, RealmMemberRole.Normal))
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

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.PostUpdate,
            Meta = { { "post_id", Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString()) } },
            AccountId = currentUser.Id.ToString(),
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<Post>> DeletePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        if (!await pub.IsMemberWithRole(post.Publisher.Id, Guid.Parse(currentUser.Id),
                Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to delete the publisher's post.");

        await ps.DeletePostAsync(post);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.PostDelete,
            Meta = { { "post_id", Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString()) } },
            AccountId = currentUser.Id.ToString(),
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }
}
