using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/apps")]
public class CustomAppController(CustomAppService customApps, PublisherService ps) : ControllerBase
{
    public record CustomAppRequest(
        [MaxLength(1024)] string? Slug,
        [MaxLength(1024)] string? Name,
        [MaxLength(4096)] string? Description,
        string? PictureId,
        string? BackgroundId,
        CustomAppStatus? Status,
        CustomAppLinks? Links,
        CustomAppOauthConfig? OauthConfig
    );

    [HttpGet]
    public async Task<IActionResult> ListApps([FromRoute] string pubName)
    {
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();
        var apps = await customApps.GetAppsByPublisherAsync(publisher.Id);
        return Ok(apps);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetApp([FromRoute] string pubName, Guid id)
    {
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var app = await customApps.GetAppAsync(id, publisherId: publisher.Id);
        if (app == null)
            return NotFound();

        return Ok(app);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateApp([FromRoute] string pubName, [FromBody] CustomAppRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Name and slug are required");

        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        if (!await ps.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to create a custom app");
        if (!await ps.HasFeature(publisher.Id, PublisherFeatureFlag.Develop))
            return StatusCode(403, "Publisher must be a developer to create a custom app");

        try
        {
            var app = await customApps.CreateAppAsync(publisher, request);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateApp(
        [FromRoute] string pubName,
        [FromRoute] Guid id,
        [FromBody] CustomAppRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();
        
        if (!await ps.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to update a custom app");

        var app = await customApps.GetAppAsync(id, publisherId: publisher.Id);
        if (app == null)
            return NotFound();

        try
        {
            app = await customApps.UpdateAppAsync(app, request);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteApp(
        [FromRoute] string pubName,
        [FromRoute] Guid id
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();
        
        if (!await ps.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to delete a custom app");

        var app = await customApps.GetAppAsync(id, publisherId: publisher.Id);
        if (app == null)
            return NotFound();

        var result = await customApps.DeleteAppAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }
}