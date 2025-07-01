using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NpgsqlTypes;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/posts")]
public class PostController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    RelationshipService rels,
    ActionLogService als
)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Post>>> ListPosts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(currentUser.Id);

        var publisher = pubName == null ? null : await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);

        var query = db.Posts.AsQueryable();
        if (publisher != null)
            query = query.Where(p => p.Publisher.Id == publisher.Id);
        query = query
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true);

        var totalCount = await query
            .CountAsync();
        var posts = await query
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Post>> GetPost(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(currentUser.Id);

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();
        post = await ps.LoadPostInfo(post, currentUser);

        // Track view - use the account ID as viewer ID if user is logged in
        await ps.IncreaseViewCount(post.Id, currentUser?.Id.ToString());

        return Ok(post);
    }

    [HttpGet("search")]
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
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(currentUser.Id);

        var queryable = db.Posts
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .AsQueryable();
        if (useVector)
            queryable = queryable.Where(p => p.SearchVector.Matches(EF.Functions.ToTsQuery(query)));
        else
            queryable = queryable.Where(p => EF.Functions.ILike(p.Title, $"%{query}%") ||
                                             EF.Functions.ILike(p.Description, $"%{query}%") ||
                                             EF.Functions.ILike(p.Content, $"%{query}%")
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

    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<List<Post>>> ListReplies(Guid id, [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(currentUser.Id);

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
    }

    [HttpPost]
    [RequiredPermission("global", "posts.create")]
    public async Task<ActionResult<Post>> CreatePost(
        [FromBody] PostRequest request,
        [FromHeader(Name = "X-Pub")] string? publisherName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        Publisher.Publisher? publisher;
        if (publisherName is null)
        {
            // Use the first personal publisher
            publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                e.AccountId == currentUser.Id && e.Type == PublisherType.Individual);
        }
        else
        {
            publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == publisherName);
            if (publisher is null) return BadRequest("Publisher was not found.");
            var member =
                await db.PublisherMembers.FirstOrDefaultAsync(e =>
                    e.AccountId == currentUser.Id && e.PublisherId == publisher.Id);
            if (member is null) return StatusCode(403, "You even wasn't a member of the publisher you specified.");
            if (member.Role < PublisherMemberRole.Editor)
                return StatusCode(403, "You need at least be an editor to post as this publisher.");
        }

        if (publisher is null) return BadRequest("Publisher was not found.");

        var post = new Post
        {
            Title = request.Title,
            Description = request.Description,
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

        try
        {
            post = await ps.PostAsync(
                currentUser,
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

        als.CreateActionLogFromRequest(
            ActionLogType.PostCreate,
            new Dictionary<string, object> { { "post_id", post.Id } }, Request
        );

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
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account.Account currentUser) return Unauthorized();
        var userFriends = await rels.ListAccountFriends(currentUser);
        var userPublishers = await pub.GetUserPublishers(currentUser.Id);

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .ThenInclude(e => e.Account)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        var isSelfReact = post.Publisher.AccountId is not null && post.Publisher.AccountId == currentUser.Id;

        var isExistingReaction = await db.PostReactions
            .AnyAsync(r => r.PostId == post.Id &&
                           r.Symbol == request.Symbol &&
                           r.AccountId == currentUser.Id);
        var reaction = new PostReaction
        {
            Symbol = request.Symbol,
            Attitude = request.Attitude,
            PostId = post.Id,
            AccountId = currentUser.Id
        };
        var isRemoving = await ps.ModifyPostVotes(
            post,
            reaction,
            currentUser,
            isExistingReaction,
            isSelfReact
        );

        if (isRemoving) return NoContent();

        als.CreateActionLogFromRequest(
            ActionLogType.PostReact,
            new Dictionary<string, object> { { "post_id", post.Id }, { "reaction", request.Symbol } }, Request
        );

        return Ok(reaction);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<Post>> UpdatePost(Guid id, [FromBody] PostRequest request)
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        if (!await pub.IsMemberWithRole(post.Publisher.Id, currentUser.Id, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to edit this publisher's post.");

        if (request.Title is not null) post.Title = request.Title;
        if (request.Description is not null) post.Description = request.Description;
        if (request.Content is not null) post.Content = request.Content;
        if (request.Visibility is not null) post.Visibility = request.Visibility.Value;
        if (request.Type is not null) post.Type = request.Type.Value;
        if (request.Meta is not null) post.Meta = request.Meta;

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

        als.CreateActionLogFromRequest(
            ActionLogType.PostUpdate,
            new Dictionary<string, object> { { "post_id", post.Id } }, Request
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<Post>> DeletePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        if (!await pub.IsMemberWithRole(post.Publisher.Id, currentUser.Id, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to delete the publisher's post.");

        await ps.DeletePostAsync(post);

        als.CreateActionLogFromRequest(
            ActionLogType.PostDelete,
            new Dictionary<string, object> { { "post_id", post.Id } }, Request
        );

        return NoContent();
    }
}