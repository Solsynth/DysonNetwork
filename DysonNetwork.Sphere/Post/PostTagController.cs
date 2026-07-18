using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts/tags")]
public class PostTagController(
    AppDatabase db,
    PostTagService tagService,
    PublisherService pub
) : ControllerBase
{
    public class CreateTagRequest
    {
        [MaxLength(128)] public string Slug { get; set; } = null!;
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    public class UpdateTagRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    private async Task<SnPublisher?> ResolvePublisherAsync(Guid accountId, string? pubName)
    {
        if (pubName is not null)
        {
            var publisher = await pub.GetPublisherByName(pubName);
            if (publisher is not null && await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return publisher;
            return null;
        }

        var settings = await db.PublishingSettings.FirstOrDefaultAsync(s => s.AccountId == accountId);
        if (settings?.DefaultPostingPublisherId is not null)
        {
            var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == settings.DefaultPostingPublisherId);
            if (publisher is not null && await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return publisher;
        }

        return await db.Publishers.FirstOrDefaultAsync(e =>
            e.AccountId == accountId && e.Type == Shared.Models.PublisherType.Individual);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> CreateTag(
        [FromBody] CreateTagRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "TAG_PUBLISHER_NOT_RESOLVED", Message = "Cannot resolve publisher. Specify one via ?pub= or set a default.", Status = 400 });

        try
        {
            var tag = await tagService.CreateTagAsync(request.Slug, request.Name, request.Description, publisher);
            return CreatedAtAction(nameof(GetTag), new { slug = tag.Slug }, tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<SnPostTag>> GetTag(string slug)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();
        return Ok(tag);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> UpdateTag(
        string slug,
        [FromBody] UpdateTagRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.UpdateTagAsync(tag.Id, request.Name, request.Description, accountId, isAdmin: false);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, ApiError.Unauthorized(ex.Message, forbidden: true));
        }
    }

    [HttpPost("{slug}/claim")]
    [AskPermission(PermissionKeys.PostsTagsClaim)]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> ClaimTag(
        string slug,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "TAG_PUBLISHER_NOT_RESOLVED", Message = "Cannot resolve publisher. Specify one via ?pub= or set a default.", Status = 400 });

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.ClaimTagAsync(tag.Id, publisher);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_CLAIM_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    /// <summary>
    /// Release ownership of a tag. Only a manager of the owning publisher can release it.
    /// Also clears protected status if set.
    /// </summary>
    [HttpPost("{slug}/release")]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> ReleaseTag(
        string slug,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "TAG_PUBLISHER_NOT_RESOLVED", Message = "Cannot resolve publisher. Specify one via ?pub= or set a default.", Status = 400 });

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.ReleaseTagAsync(tag.Id, publisher, accountId);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("manager", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not owned", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, ApiError.Unauthorized(ex.Message, forbidden: true));
            return BadRequest(new ApiError { Code = "TAG_RELEASE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    public class SetProtectedRequest
    {
        public bool IsProtected { get; set; }
    }

    /// <summary>
    /// Enable or disable protection on an owned tag. Consumes protected-tag quota when enabling.
    /// </summary>
    [HttpPatch("{slug}/protect")]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> SetProtected(
        string slug,
        [FromBody] SetProtectedRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "TAG_PUBLISHER_NOT_RESOLVED", Message = "Cannot resolve publisher. Specify one via ?pub= or set a default.", Status = 400 });

        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
            return StatusCode(403, ApiError.Unauthorized("You must be a manager or above of the owning publisher.", forbidden: true));

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        if (tag.OwnerPublisherId != publisher.Id)
            return StatusCode(403, ApiError.Unauthorized("This tag is not owned by the specified publisher.", forbidden: true));

        try
        {
            tag = await tagService.SetProtectedAsync(tag.Id, request.IsProtected, publisher);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_PROTECT_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    /// <summary>
    /// Publisher-scoped protected-tag quota and owned tag list.
    /// </summary>
    /// <remarks>
    /// Path is intentionally <c>/quota</c> (not under a tag slug). The optional
    /// legacy alias <c>/{slug}/quota</c> remains for older clients and ignores slug.
    /// </remarks>
    [HttpGet("quota")]
    [HttpGet("{slug}/quota")]
    [Authorize]
    public async Task<ActionResult<ResourceQuotaResponse<ProtectedTagQuotaRecord>>> GetProtectedTagQuota(
        [FromQuery(Name = "pub")] string? pubName,
        string? slug = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "TAG_PUBLISHER_NOT_RESOLVED", Message = "Cannot resolve publisher. Specify one via ?pub= or set a default.", Status = 400 });

        var quota = await tagService.GetProtectedTagQuotaAsync(publisher);
        return Ok(quota);
    }
}

[ApiController]
[Route("/api/admin/tags")]
[Authorize]
public class PostTagAdminController(
    AppDatabase db,
    PostTagService tagService,
    RemoteActionLogService als
) : ControllerBase
{
    public class CreateAdminTagRequest
    {
        [MaxLength(128)] public string Slug { get; set; } = null!;
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public Guid? OwnerPublisherId { get; set; }
    }

    public class AssignTagRequest
    {
        public Guid PublisherId { get; set; }
    }

    public class SetProtectedRequest
    {
        public bool IsProtected { get; set; }
    }

    public class SetEventRequest
    {
        public bool IsEvent { get; set; }
        public Instant? EndsAt { get; set; }
    }

    public class AdminUpdateTagRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    /// <summary>Backward-compatible route for older clients. </summary>
    [HttpGet("/api/admin/posts/tags")]
    [HttpGet]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<List<SnPostTag>>> ListTags(
        [FromQuery] string? query = null,
        [FromQuery] Guid? ownerPublisherId = null,
        [FromQuery] bool? isProtected = null,
        [FromQuery] bool? isEvent = null,
        [FromQuery] bool? unowned = null,
        [FromQuery] string? order = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var tagsQuery = db.PostTags
            .AsNoTracking()
            .Include(t => t.OwnerPublisher)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            tagsQuery = tagsQuery.Where(t =>
                EF.Functions.ILike(t.Slug, $"%{probe}%") ||
                (t.Name != null && EF.Functions.ILike(t.Name, $"%{probe}%")) ||
                (t.Description != null && EF.Functions.ILike(t.Description, $"%{probe}%")));
        }

        if (ownerPublisherId.HasValue)
            tagsQuery = tagsQuery.Where(t => t.OwnerPublisherId == ownerPublisherId.Value);
        if (unowned == true)
            tagsQuery = tagsQuery.Where(t => t.OwnerPublisherId == null);
        if (isProtected.HasValue)
            tagsQuery = tagsQuery.Where(t => t.IsProtected == isProtected.Value);
        if (isEvent.HasValue)
            tagsQuery = tagsQuery.Where(t => t.IsEvent == isEvent.Value);

        tagsQuery = order switch
        {
            "usage" => tagsQuery.OrderByDescending(t => t.Posts.Count).ThenBy(t => t.Slug),
            "name" => tagsQuery.OrderBy(t => t.Name ?? t.Slug),
            "created" => tagsQuery.OrderByDescending(t => t.CreatedAt),
            _ => tagsQuery.OrderByDescending(t => t.UpdatedAt)
        };

        var total = await tagsQuery.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var rows = await tagsQuery
            .Skip(offset)
            .Take(take)
            .Select(t => new { Tag = t, PostCount = t.Posts.Count })
            .ToListAsync(HttpContext.RequestAborted);

        var result = rows.Select(x =>
        {
            x.Tag.Usage = x.PostCount;
            return x.Tag;
        }).ToList();

        return Ok(result);
    }

    [HttpGet("/api/admin/posts/tags/{slug}")]
    [HttpGet("{slug}")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPostTag>> GetTag(string slug)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        tag.Usage = await db.Posts
            .Where(p => p.Tags.Any(t => t.Id == tag.Id))
            .CountAsync(HttpContext.RequestAborted);

        return Ok(tag);
    }

    [HttpPost("/api/admin/posts/tags")]
    [HttpPost]
    [AskPermission(PermissionKeys.PostsTagsCreate)]
    public async Task<ActionResult<SnPostTag>> CreateTag([FromBody] CreateAdminTagRequest request)
    {
        SnPublisher? owner = null;
        if (request.OwnerPublisherId.HasValue)
        {
            owner = await db.Publishers.FirstOrDefaultAsync(
                p => p.Id == request.OwnerPublisherId.Value,
                HttpContext.RequestAborted
            );
            if (owner is null)
                return BadRequest(new ApiError { Code = "TAG_OWNER_PUBLISHER_NOT_FOUND", Message = "Owner publisher not found.", Status = 400 });
        }

        try
        {
            var tag = await tagService.CreateTagAsync(
                request.Slug,
                request.Name,
                request.Description,
                owner
            );
            LogTagAction("create", tag.Id, new Dictionary<string, object>
            {
                ["slug"] = tag.Slug
            });
            return CreatedAtAction(nameof(GetTag), new { slug = tag.Slug }, tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("/api/admin/posts/tags/{slug}/assign")]
    [HttpPost("{slug}/assign")]
    [AskPermission(PermissionKeys.PostsTagsAssign)]
    public async Task<ActionResult<SnPostTag>> AssignTag(string slug, [FromBody] AssignTagRequest request)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.AssignTagAsync(tag.Id, request.PublisherId);
            LogTagAction("assign", tag.Id, new Dictionary<string, object>
            {
                ["publisher_id"] = request.PublisherId.ToString()
            });
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_ASSIGN_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpDelete("/api/admin/posts/tags/{slug}/assign")]
    [HttpDelete("{slug}/assign")]
    [AskPermission(PermissionKeys.PostsTagsAssign)]
    public async Task<ActionResult<SnPostTag>> UnassignTag(string slug)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.UnassignTagAsync(tag.Id);
            LogTagAction("unassign", tag.Id);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_UNASSIGN_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPatch("/api/admin/posts/tags/{slug}/protect")]
    [HttpPatch("{slug}/protect")]
    [AskPermission(PermissionKeys.PostsTagsProtect)]
    public async Task<ActionResult<SnPostTag>> SetProtected(string slug, [FromBody] SetProtectedRequest request)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        if (tag.OwnerPublisherId is null)
            return BadRequest(new ApiError { Code = "TAG_NO_OWNER", Message = "Tag has no owner. Assign ownership first.", Status = 400 });

        var publisher = await db.Publishers.FirstOrDefaultAsync(
            p => p.Id == tag.OwnerPublisherId.Value,
            HttpContext.RequestAborted
        );
        if (publisher is null) return BadRequest(new ApiError { Code = "TAG_OWNER_PUBLISHER_NOT_FOUND", Message = "Owner publisher not found.", Status = 400 });

        try
        {
            tag = await tagService.SetProtectedAsync(tag.Id, request.IsProtected, publisher);
            LogTagAction("set_protected", tag.Id, new Dictionary<string, object>
            {
                ["is_protected"] = request.IsProtected
            });
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_PROTECT_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPatch("/api/admin/posts/tags/{slug}/event")]
    [HttpPatch("{slug}/event")]
    [AskPermission(PermissionKeys.PostsTagsEvent)]
    public async Task<ActionResult<SnPostTag>> SetEvent(string slug, [FromBody] SetEventRequest request)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.SetEventAsync(tag.Id, request.IsEvent, request.EndsAt);
            LogTagAction("set_event", tag.Id, new Dictionary<string, object>
            {
                ["is_event"] = request.IsEvent,
                ["ends_at"] = request.EndsAt?.ToString() ?? string.Empty
            });
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_EVENT_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPatch("/api/admin/posts/tags/{slug}")]
    [HttpPatch("{slug}")]
    [AskPermission(PermissionKeys.PostsTagsUpdate)]
    public async Task<ActionResult<SnPostTag>> AdminUpdateTag(string slug, [FromBody] AdminUpdateTagRequest request)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            tag = await tagService.UpdateTagAsync(
                tag.Id,
                request.Name,
                request.Description,
                Guid.Parse(currentUser.Id),
                isAdmin: true
            );
            LogTagAction("update", tag.Id);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_ADMIN_UPDATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpDelete("/api/admin/posts/tags/{slug}")]
    [HttpDelete("{slug}")]
    [AskPermission(PermissionKeys.PostsTagsDelete)]
    public async Task<IActionResult> DeleteTag(string slug)
    {
        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            await tagService.DeleteTagAsync(tag.Id);
            LogTagAction("delete", tag.Id, new Dictionary<string, object>
            {
                ["slug"] = tag.Slug
            });
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "TAG_DELETE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    private void LogTagAction(string operation, Guid tagId, Dictionary<string, object>? extraMeta = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return;

        var meta = new Dictionary<string, object>
        {
            ["tag_id"] = tagId.ToString(),
            ["operation"] = operation
        };
        if (extraMeta is not null)
        {
            foreach (var (key, value) in extraMeta)
                meta[key] = value;
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostsTagsAdmin,
            meta,
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );
    }
}
