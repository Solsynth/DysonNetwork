using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.MiniApp;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/miniapps")]
public class MiniAppController(MiniAppService miniAppService, Identity.DeveloperService ds, DevProjectService projectService)
    : ControllerBase
{
    public record MiniAppRequest(
        [MaxLength(1024)] string? Slug,
        MiniAppStage? Stage,
        MiniAppManifest? Manifest
    );

    public record CreateMiniAppRequest(
        [Required]
        [MinLength(2)]
        [MaxLength(1024)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Slug can only contain letters, numbers, underscores, and hyphens.")]
        string Slug,

        MiniAppStage Stage = MiniAppStage.Development,

        [Required] MiniAppManifest Manifest = null!
    );

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListMiniApps([FromRoute] string pubName, [FromRoute] Guid projectId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound("Developer not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to list mini apps");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound("Project not found or you don't have access");

        var miniApps = await miniAppService.GetMiniAppsByProjectAsync(projectId);
        return Ok(miniApps);
    }

    [HttpGet("{miniAppId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetMiniApp([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid miniAppId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound("Developer not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to view mini app details");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != projectId)
            return NotFound("Mini app not found");

        return Ok(miniApp);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateMiniApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromBody] CreateMiniAppRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to create a mini app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        try
        {
            var miniApp = await miniAppService.CreateMiniAppAsync(projectId, request.Slug, request.Stage, request.Manifest);
            return CreatedAtAction(
                nameof(GetMiniApp),
                new { pubName, projectId, miniAppId = miniApp.Id },
                miniApp
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{miniAppId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateMiniApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid miniAppId,
        [FromBody] MiniAppRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to update a mini app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != projectId)
            return NotFound("Mini app not found");

        try
        {
            miniApp = await miniAppService.UpdateMiniAppAsync(miniApp, request.Slug, request.Stage, request.Manifest);
            return Ok(miniApp);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{miniAppId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteMiniApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid miniAppId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to delete a mini app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != projectId)
            return NotFound("Mini app not found");

        var result = await miniAppService.DeleteMiniAppAsync(miniAppId);
        if (!result)
            return NotFound("Failed to delete mini app");

        return NoContent();
    }
}
