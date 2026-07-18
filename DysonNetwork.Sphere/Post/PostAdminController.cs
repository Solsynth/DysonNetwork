using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/admin/posts")]
[Authorize]
[ApiFeature("admin.posts", Revision = 1)]
[ApiFeature("admin.posts.lock", Revision = 1)]
[ApiFeature("admin.posts.shadowban", Revision = 1)]
public class PostAdminController(
    AppDatabase db,
    PostService postService,
    RemoteActionLogService als
) : ControllerBase
{
    public class SetPostVisibilityRequest
    {
        public PostVisibility Visibility { get; set; }
    }

    public class SetPostShadowbanRequest
    {
        public PostShadowbanReason Reason { get; set; }
    }

    public class ModeratePostRequest
    {
        [MaxLength(4096)] public string? Reason { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<List<SnPost>>> ListPosts(
        [FromQuery] string? query = null,
        [FromQuery] Guid? publisherId = null,
        [FromQuery] Guid? realmId = null,
        [FromQuery] PostVisibility? visibility = null,
        [FromQuery] PostShadowbanReason? shadowbanReason = null,
        [FromQuery] bool? locked = null,
        [FromQuery] bool? drafted = null,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var queryable = db.Posts
            .AsNoTracking()
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            queryable = queryable.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, $"%{probe}%")) ||
                (p.Description != null && EF.Functions.ILike(p.Description, $"%{probe}%")) ||
                (p.Content != null && EF.Functions.ILike(p.Content, $"%{probe}%")));
        }

        if (publisherId.HasValue)
            queryable = queryable.Where(p => p.PublisherId == publisherId.Value);
        if (realmId.HasValue)
            queryable = queryable.Where(p => p.RealmId == realmId.Value);
        if (visibility.HasValue)
            queryable = queryable.Where(p => p.Visibility == visibility.Value);
        if (shadowbanReason.HasValue)
            queryable = queryable.Where(p => p.ShadowbanReason == shadowbanReason.Value);
        if (locked.HasValue)
            queryable = locked.Value
                ? queryable.Where(p => p.LockedAt != null)
                : queryable.Where(p => p.LockedAt == null);
        if (drafted.HasValue)
            queryable = drafted.Value
                ? queryable.Where(p => p.DraftedAt != null)
                : queryable.Where(p => p.DraftedAt == null);

        var total = await queryable.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var posts = await queryable
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(posts);
    }

    [HttpGet("{id:guid}")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPost>> GetPost(Guid id)
    {
        var post = await db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.RepliedPost)
            .Include(p => p.ForwardedPost)
            .Include(p => p.FeaturedRecords)
            .FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();

        post = await postService.LoadPostInfo(post, null, truncate: false);
        return Ok(post);
    }

    [HttpGet("{id:guid}/lock")]
    public async Task<ActionResult> GetLockStatus(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        return Ok(new { locked = post.LockedAt.HasValue, lockedAt = post.LockedAt });
    }

    [HttpPost("{id:guid}/lock")]
    [AskPermission(PermissionKeys.PostsLock)]
    public async Task<ActionResult> LockPost(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        if (post.LockedAt is not null)
            return BadRequest(new ApiError { Code = "POST_ALREADY_LOCKED", Message = "Post is already locked.", Status = 400 });

        post.LockedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return Ok(new { locked = true, lockedAt = post.LockedAt });
    }

    [HttpDelete("{id:guid}/lock")]
    [AskPermission(PermissionKeys.PostsLock)]
    public async Task<ActionResult> UnlockPost(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        if (post.LockedAt is null)
            return BadRequest(new ApiError { Code = "POST_NOT_LOCKED", Message = "Post is not locked.", Status = 400 });

        post.LockedAt = null;
        await db.SaveChangesAsync();

        return Ok(new { locked = false });
    }

    [HttpPost("{id:guid}/visibility")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPost>> SetVisibility(
        Guid id,
        [FromBody] SetPostVisibilityRequest request
    )
    {
        var post = await db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Include(p => p.FeaturedRecords)
            .FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();

        post.Visibility = request.Visibility;

        try
        {
            post = await postService.UpdatePostAsync(post);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "POST_VISIBILITY_UPDATE_FAILED", Message = ex.Message, Status = 400 });
        }

        LogPostAction(ActionLogType.PostModerate, post.Id, new Dictionary<string, object>
        {
            ["operation"] = "set_visibility",
            ["visibility"] = request.Visibility.ToString()
        });

        return Ok(post);
    }

    [HttpPost("{id:guid}/shadowban")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPost>> ShadowbanPost(
        Guid id,
        [FromBody] SetPostShadowbanRequest request
    )
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();

        if (request.Reason == PostShadowbanReason.None)
            return BadRequest(new ApiError { Code = "POST_SHADOWBAN_USE_DELETE", Message = "Use DELETE to clear a shadowban.", Status = 400 });

        post.ShadowbanReason = request.Reason;
        post.ShadowbannedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPostAction(ActionLogType.PostModerate, post.Id, new Dictionary<string, object>
        {
            ["operation"] = "shadowban",
            ["reason"] = request.Reason.ToString()
        });

        return Ok(post);
    }

    [HttpDelete("{id:guid}/shadowban")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPost>> UnshadowbanPost(Guid id)
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();

        post.ShadowbanReason = PostShadowbanReason.None;
        post.ShadowbannedAt = null;
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogPostAction(ActionLogType.PostModerate, post.Id, new Dictionary<string, object>
        {
            ["operation"] = "unshadowban"
        });

        return Ok(post);
    }

    [HttpPost("{id:guid}/realm/remove")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPost>> RemovePostFromRealm(
        Guid id,
        [FromBody] ModeratePostRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var post = await db.Posts
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();
        if (post.RealmId is null)
            return BadRequest(new ApiError { Code = "POST_NOT_IN_REALM", Message = "This post is not linked to a realm.", Status = 400 });
        var realmId = post.RealmId.Value;

        var existingLog = await db.RealmPostModerationLogs
            .AnyAsync(
                l => l.PostId == post.Id && l.RealmId == realmId && l.DeletedAt == null,
                HttpContext.RequestAborted
            );
        if (existingLog)
            return BadRequest(new ApiError { Code = "POST_ALREADY_REMOVED_FROM_REALM", Message = "This post has already been removed from the realm.", Status = 400 });

        db.RealmPostModerationLogs.Add(new SnRealmPostModerationLog
        {
            RealmId = realmId,
            PostId = post.Id,
            ModeratorAccountId = Guid.Parse(currentUser.Id),
            Reason = request?.Reason
        });
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        var result = await postService.RemovePostFromRealmAsync(
            post,
            Guid.Parse(currentUser.Id),
            request?.Reason
        );

        LogPostAction(ActionLogType.PostModerate, post.Id, new Dictionary<string, object>
        {
            ["operation"] = "remove_from_realm",
            ["realm_id"] = realmId.ToString(),
            ["reason"] = request?.Reason ?? string.Empty
        });

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [AskPermission(PermissionKeys.PostsDelete)]
    public async Task<IActionResult> DeletePost(Guid id)
    {
        var post = await db.Posts
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (post is null)
            return NotFound();

        await postService.DeletePostAsync(post);

        LogPostAction(ActionLogType.PostDelete, post.Id);
        return NoContent();
    }

    [HttpPost("{id:guid}/lock/batch")]
    [AskPermission(PermissionKeys.PostsLock)]
    public async Task<ActionResult> LockPostsBatch([FromBody] List<Guid> ids)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var posts = await db.Posts.Where(p => ids.Contains(p.Id)).ToListAsync();

        foreach (var post in posts)
        {
            post.LockedAt ??= now;
        }

        await db.SaveChangesAsync();

        return Ok(new { locked = posts.Count });
    }

    [HttpDelete("lock/batch")]
    [AskPermission(PermissionKeys.PostsLock)]
    public async Task<ActionResult> UnlockPostsBatch([FromBody] List<Guid> ids)
    {
        var posts = await db.Posts.Where(p => ids.Contains(p.Id)).ToListAsync();

        foreach (var post in posts)
        {
            post.LockedAt = null;
        }

        await db.SaveChangesAsync();

        return Ok(new { unlocked = posts.Count });
    }

    private void LogPostAction(string action, Guid postId, Dictionary<string, object>? extraMeta = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return;

        var meta = new Dictionary<string, object> { ["post_id"] = postId.ToString() };
        if (extraMeta is not null)
        {
            foreach (var (key, value) in extraMeta)
                meta[key] = value;
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            action,
            meta,
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );
    }
}
