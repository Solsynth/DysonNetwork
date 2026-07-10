using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Develop.Models;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.MiniApp;

[ApiController]
[Route("/api/private/miniapps")]
public class MiniAppController(
    MiniAppService miniAppService,
    MiniAppStorageService storageService,
    Identity.DeveloperService ds,
    DevProjectService projectService)
    : ControllerBase
{
    public record MiniAppRequest(
        [MaxLength(1024)] string? Slug,
        MiniAppStage? Stage,
        MiniAppManifest? Manifest,
        string? IconId,
        string? BackgroundId
    );

    public record CreateMiniAppRequest(
        [Required]
        [MinLength(2)]
        [MaxLength(1024)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Slug can only contain letters, numbers, underscores, and hyphens.")]
        string Slug,

        MiniAppStage Stage = MiniAppStage.Development,

        [Required] MiniAppManifest Manifest = null!,
        string? IconId = null,
        string? BackgroundId = null
    );

    public class UploadPackageRequest
    {
        [Required] public IFormFile File { get; set; } = null!;
    }

    private static string? ValidatePluginManifest(MiniAppManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            !Regex.IsMatch(manifest.Id, "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)+$"))
            return "Manifest id must be a reverse-domain identifier.";

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return "Manifest name is required.";

        return null;
    }

    [HttpPost("{miniAppId:guid}/package")]
    [Authorize]
    [AskPermission(PermissionKeys.MiniAppsPackageUpload)]
    [RequestSizeLimit(MiniAppStorageConfiguration.MaxMultipartRequestSizeBytes)]
    public async Task<IActionResult> UploadPackage(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid miniAppId,
        [FromForm] UploadPackageRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to upload a plugin file");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp is null || miniApp.ProjectId != proj)
            return NotFound("Mini app not found");

        try
        {
            var result = await storageService.SavePackageAsync(miniAppId, request.File, cancellationToken);
            miniApp.PackageUrl = result.Url;
            miniApp.PackageStorageKey = result.Key;
            miniApp.PackageSha256 = result.Sha256;
            miniApp.PackageSize = result.Size;
            await miniAppService.SaveAsync(miniApp, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    [Authorize]
    [AskPermission(PermissionKeys.MiniAppsView)]
    public async Task<IActionResult> ListMiniApps([FromQuery(Name = "dev")] string dev, [FromQuery(Name = "proj")] Guid proj)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound("Developer not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to list mini apps");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound("Project not found or you don't have access");

        var miniApps = await miniAppService.GetMiniAppsByProjectAsync(proj);
        return Ok(miniApps);
    }

    [HttpGet("{miniAppId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.MiniAppsView)]
    public async Task<IActionResult> GetMiniApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid miniAppId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound("Developer not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyViewer))
            return StatusCode(403, "You must be a viewer of the developer to view mini app details");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null) return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != proj)
            return NotFound("Mini app not found");

        return Ok(miniApp);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateMiniApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromBody] CreateMiniAppRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to create a mini app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var manifestError = ValidatePluginManifest(request.Manifest);
        if (manifestError is not null)
            return BadRequest(manifestError);

        try
        {
            var miniApp = await miniAppService.CreateMiniAppAsync(
                proj,
                request.Slug,
                request.Stage,
                request.Manifest,
                request.IconId,
                request.BackgroundId);
            return CreatedAtAction(nameof(GetMiniApp), new { dev, proj, miniAppId = miniApp.Id }, miniApp);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{miniAppId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.MiniAppsUpdate)]
    public async Task<IActionResult> UpdateMiniApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid miniAppId,
        [FromBody] MiniAppRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to update a mini app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        if (request.Manifest is not null)
        {
            var manifestError = ValidatePluginManifest(request.Manifest);
            if (manifestError is not null)
                return BadRequest(manifestError);
        }

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != proj)
            return NotFound("Mini app not found");

        try
        {
            miniApp = await miniAppService.UpdateMiniAppAsync(
                miniApp,
                request.Slug,
                request.Stage,
                request.Manifest,
                request.IconId,
                request.BackgroundId);
            return Ok(miniApp);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{miniAppId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.MiniAppsDelete)]
    public async Task<IActionResult> DeleteMiniApp(
        [FromQuery(Name = "dev")] string dev,
        [FromQuery(Name = "proj")] Guid proj,
        [FromRoute] Guid miniAppId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, "You must be an editor of the developer to delete a mini app");

        var project = await projectService.GetProjectAsync(proj, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var miniApp = await miniAppService.GetMiniAppByIdAsync(miniAppId);
        if (miniApp == null || miniApp.ProjectId != proj)
            return NotFound("Mini app not found");

        var result = await miniAppService.DeleteMiniAppAsync(miniAppId);
        if (!result)
            return NotFound("Failed to delete mini app");

        return NoContent();
    }
}
