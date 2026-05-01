using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest("Cannot resolve publisher. Specify one via ?pub= or set a default.");

        try
        {
            var tag = await tagService.CreateTagAsync(request.Slug, request.Name, request.Description, publisher);
            return CreatedAtAction(nameof(GetTag), new { slug = tag.Slug }, tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
            return StatusCode(403, ex.Message);
        }
    }

    [HttpPost("{slug}/claim")]
    [Authorize]
    public async Task<ActionResult<SnPostTag>> ClaimTag(
        string slug,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest("Cannot resolve publisher. Specify one via ?pub= or set a default.");

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.ClaimTagAsync(tag.Id, publisher);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{slug}/quota")]
    [Authorize]
    public async Task<ActionResult<ResourceQuotaResponse<ProtectedTagQuotaRecord>>> GetProtectedTagQuota(
        string slug,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await ResolvePublisherAsync(accountId, pubName);
        if (publisher is null)
            return BadRequest("Cannot resolve publisher.");

        var quota = await tagService.GetProtectedTagQuotaAsync(publisher);
        return Ok(quota);
    }
}

[ApiController]
[Route("/api/admin/posts/tags")]
[Authorize]
public class PostTagAdminController(
    AppDatabase db,
    PostTagService tagService,
    PublisherService pub
) : ControllerBase
{
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

    public class UpdateTagRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    private async Task<bool> IsAdminAsync()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return false;
        if (currentUser.IsSuperuser) return true;

        using var scope = HttpContext.RequestServices.CreateScope();
        var permissionService = scope.ServiceProvider.GetRequiredService<DyPermissionService.DyPermissionServiceClient>();
        var response = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
        {
            Actor = currentUser.Id.ToString(),
            Key = "posts.tags.admin"
        });
        return response.HasPermission;
    }

    [HttpPost("{slug}/assign")]
    public async Task<ActionResult<SnPostTag>> AssignTag(string slug, [FromBody] AssignTagRequest request)
    {
        if (!await IsAdminAsync()) return StatusCode(403, "Admin permission required.");

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.AssignTagAsync(tag.Id, request.PublisherId);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{slug}/protect")]
    public async Task<ActionResult<SnPostTag>> SetProtected(string slug, [FromBody] SetProtectedRequest request)
    {
        if (!await IsAdminAsync()) return StatusCode(403, "Admin permission required.");

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        if (tag.OwnerPublisherId is null)
            return BadRequest("Tag has no owner. Assign ownership first.");

        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == tag.OwnerPublisherId.Value);
        if (publisher is null) return BadRequest("Owner publisher not found.");

        try
        {
            tag = await tagService.SetProtectedAsync(tag.Id, request.IsProtected, publisher);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{slug}/event")]
    public async Task<ActionResult<SnPostTag>> SetEvent(string slug, [FromBody] SetEventRequest request)
    {
        if (!await IsAdminAsync()) return StatusCode(403, "Admin permission required.");

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        try
        {
            tag = await tagService.SetEventAsync(tag.Id, request.IsEvent, request.EndsAt);
            return Ok(tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{slug}")]
    public async Task<ActionResult<SnPostTag>> AdminUpdateTag(string slug, [FromBody] UpdateTagRequest request)
    {
        if (!await IsAdminAsync()) return StatusCode(403, "Admin permission required.");

        var tag = await tagService.FindBySlugAsync(slug);
        if (tag is null) return NotFound();

        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        tag = await tagService.UpdateTagAsync(tag.Id, request.Name, request.Description, Guid.Parse(currentUser.Id), isAdmin: true);
        return Ok(tag);
    }
}
