using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/posts")]
public class PostController(AppDatabase db, PostService ps, RelationshipService rels) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Post>>> ListPosts([FromQuery] int offset = 0, [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);

        var totalCount = await db.Posts
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .CountAsync();
        var posts = await db.Posts
            .Include(e => e.Publisher)
            .Include(e => e.ThreadedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Attachments)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = PostService.TruncatePostContent(posts);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount =
                reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Post>> GetPost(long id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.RepliedPost)
            .Include(e => e.ThreadedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.Attachments)
            .FilterWithVisibility(currentUser, userFriends)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        post.ReactionsCount = await ps.GetPostReactionMap(post.Id);

        return Ok(post);
    }

    [HttpGet("{id:long}/replies")]
    public async Task<ActionResult<List<Post>>> ListReplies(long id, [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);

        var parent = await db.Posts
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (parent is null) return NotFound();

        var totalCount = await db.Posts
            .Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .CountAsync();
        var posts = await db.Posts
            .Where(e => e.RepliedPostId == id)
            .Include(e => e.Publisher)
            .Include(e => e.ThreadedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Attachments)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = PostService.TruncatePostContent(posts);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount = reactionMaps[post.Id];

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    public class PostRequest
    {
        [MaxLength(1024)] public string? Title { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? Content { get; set; }
        public PostVisibility? Visibility { get; set; }
        public PostType? Type { get; set; }
        [MaxLength(16)] public List<string>? Tags { get; set; }
        [MaxLength(8)] public List<string>? Categories { get; set; }
        [MaxLength(32)] public List<string>? Attachments { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Instant? PublishedAt { get; set; }
        public long? RepliedPostId { get; set; }
        public long? ForwardedPostId { get; set; }
    }

    [HttpPost]
    [RequiredPermission("global", "posts.create")]
    public async Task<ActionResult<Post>> CreatePost(
        [FromBody] PostRequest request,
        [FromHeader(Name = "X-Pub")] string? publisherName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        Publisher? publisher;
        if (publisherName is null)
        {
            // Use the first personal publisher
            publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                e.AccountId == currentUser.Id && e.PublisherType == PublisherType.Individual);
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

        return post;
    }

    public class PostReactionRequest
    {
        [MaxLength(256)] public string Symbol { get; set; } = null!;
        public PostReactionAttitude Attitude { get; set; }
    }

    [HttpPost("{id:long}/reactions")]
    [Authorize]
    [RequiredPermission("global", "posts.react")]
    public async Task<ActionResult<PostReaction>> ReactPost(long id, [FromBody] PostReactionRequest request)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account.Account currentUser) return Unauthorized();
        var userFriends = await rels.ListAccountFriends(currentUser);

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends)
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
        var isRemoving = await ps.ModifyPostVotes(post, reaction, isExistingReaction, isSelfReact);

        if (isRemoving) return NoContent();
        return Ok(reaction);
    }

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<Post>> UpdatePost(long id, [FromBody] PostRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Publisher.Picture)
            .Include(e => e.Publisher.Background)
            .Include(e => e.Attachments)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        var member = await db.PublisherMembers
            .FirstOrDefaultAsync(e => e.AccountId == currentUser.Id && e.PublisherId == post.Publisher.Id);
        if (member is null) return StatusCode(403, "You even wasn't a member of the publisher you specified.");
        if (member.Role < PublisherMemberRole.Editor)
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

        return Ok(post);
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<Post>> DeletePost(long id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var post = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        var member = await db.PublisherMembers
            .FirstOrDefaultAsync(e => e.AccountId == currentUser.Id && e.PublisherId == post.Publisher.Id);
        if (member is null) return StatusCode(403, "You even wasn't a member of the publisher you specified.");
        if (member.Role < PublisherMemberRole.Editor)
            return StatusCode(403, "You need at least be an editor to delete the publisher's post.");

        await ps.DeletePostAsync(post);

        return NoContent();
    }
}