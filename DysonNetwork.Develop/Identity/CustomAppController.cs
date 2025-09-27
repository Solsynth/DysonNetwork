using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/apps")]
public class CustomAppController(CustomAppService customApps, DeveloperService ds, DevProjectService projectService)
    : ControllerBase
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

    public record CreateSecretRequest(
        [MaxLength(4096)] string? Description,
        TimeSpan? ExpiresIn = null,
        bool IsOidc = false
    );

    public record SecretResponse(
        string Id,
        string? Secret,
        string? Description,
        Instant? ExpiresAt,
        bool IsOidc,
        Instant CreatedAt,
        Instant UpdatedAt
    );

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListApps([FromRoute] string pubName, [FromRoute] Guid projectId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, PublisherMemberRole.Viewer))
            return StatusCode(403, "You must be a viewer of the developer to list custom apps");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound();

        var apps = await customApps.GetAppsByProjectAsync(projectId);
        return Ok(apps);
    }

    [HttpGet("{appId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetApp([FromRoute] string pubName, [FromRoute] Guid projectId,
        [FromRoute] Guid appId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        
        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();
        
        var accountId = Guid.Parse(currentUser.Id);
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, PublisherMemberRole.Viewer))
            return StatusCode(403, "You must be a viewer of the developer to list custom apps");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound();

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound();

        return Ok(app);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromBody] CustomAppRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to create a custom app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Name and slug are required");

        try
        {
            var app = await customApps.CreateAppAsync(projectId, request);
            if (app == null)
                return BadRequest("Failed to create app");

            return CreatedAtAction(
                nameof(GetApp),
                new { pubName, projectId, appId = app.Id },
                app
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{appId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId,
        [FromBody] CustomAppRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to update a custom app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
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

    [HttpDelete("{appId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteApp(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to delete a custom app");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound();

        var result = await customApps.DeleteAppAsync(appId);
        if (!result)
            return NotFound();

        return NoContent();
    }

    [HttpGet("{appId:guid}/secrets")]
    [Authorize]
    public async Task<IActionResult> ListSecrets(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to view app secrets");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound("App not found");

        var secrets = await customApps.GetAppSecretsAsync(appId);
        return Ok(secrets.Select(s => new SecretResponse(
            s.Id.ToString(),
            null,
            s.Description,
            s.ExpiredAt,
            s.IsOidc,
            s.CreatedAt,
            s.UpdatedAt
        )));
    }

    [HttpPost("{appId:guid}/secrets")]
    [Authorize]
    public async Task<IActionResult> CreateSecret(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId,
        [FromBody] CreateSecretRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to create app secrets");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound("App not found");

        try
        {
            var secret = await customApps.CreateAppSecretAsync(new CustomAppSecret
            {
                AppId = appId,
                Description = request.Description,
                ExpiredAt = request.ExpiresIn.HasValue
                    ? NodaTime.SystemClock.Instance.GetCurrentInstant()
                        .Plus(Duration.FromTimeSpan(request.ExpiresIn.Value))
                    : (NodaTime.Instant?)null,
                IsOidc = request.IsOidc
            });

            return CreatedAtAction(
                nameof(GetSecret),
                new { pubName, projectId, appId, secretId = secret.Id },
                new SecretResponse(
                    secret.Id.ToString(),
                    secret.Secret,
                    secret.Description,
                    secret.ExpiredAt,
                    secret.IsOidc,
                    secret.CreatedAt,
                    secret.UpdatedAt
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{appId:guid}/secrets/{secretId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetSecret(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to view app secrets");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound("App not found");

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound("Secret not found");

        return Ok(new SecretResponse(
            secret.Id.ToString(),
            null,
            secret.Description,
            secret.ExpiredAt,
            secret.IsOidc,
            secret.CreatedAt,
            secret.UpdatedAt
        ));
    }

    [HttpDelete("{appId:guid}/secrets/{secretId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteSecret(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to delete app secrets");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound("App not found");

        var secret = await customApps.GetAppSecretAsync(secretId, appId);
        if (secret == null)
            return NotFound("Secret not found");

        var result = await customApps.DeleteAppSecretAsync(secretId, appId);
        if (!result)
            return NotFound("Failed to delete secret");

        return NoContent();
    }

    [HttpPost("{appId:guid}/secrets/{secretId:guid}/rotate")]
    [Authorize]
    public async Task<IActionResult> RotateSecret(
        [FromRoute] string pubName,
        [FromRoute] Guid projectId,
        [FromRoute] Guid appId,
        [FromRoute] Guid secretId,
        [FromBody] CreateSecretRequest? request = null)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to rotate app secrets");

        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        var app = await customApps.GetAppAsync(appId, projectId);
        if (app == null)
            return NotFound("App not found");

        try
        {
            var secret = await customApps.RotateAppSecretAsync(new CustomAppSecret
            {
                Id = secretId,
                AppId = appId,
                Description = request?.Description,
                ExpiredAt = request?.ExpiresIn.HasValue == true
                    ? NodaTime.SystemClock.Instance.GetCurrentInstant()
                        .Plus(Duration.FromTimeSpan(request.ExpiresIn.Value))
                    : (NodaTime.Instant?)null,
                IsOidc = request?.IsOidc ?? false
            });

            return Ok(new SecretResponse(
                secret.Id.ToString(),
                secret.Secret,
                secret.Description,
                secret.ExpiredAt,
                secret.IsOidc,
                secret.CreatedAt,
                secret.UpdatedAt
            ));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}