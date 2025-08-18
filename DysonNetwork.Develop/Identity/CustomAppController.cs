using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/developers/{pubName}/projects/{projectId:guid}/apps")]
public class CustomAppController(CustomAppService customApps, DeveloperService ds, DevProjectService projectService) : ControllerBase
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
    public async Task<IActionResult> ListApps([FromRoute] string pubName, [FromRoute] Guid projectId)
    {
        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();
        
        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null) return NotFound();
        
        var apps = await customApps.GetAppsByProjectAsync(projectId);
        return Ok(apps);
    }

    [HttpGet("{appId:guid}")]
    public async Task<IActionResult> GetApp([FromRoute] string pubName, [FromRoute] Guid projectId, [FromRoute] Guid appId)
    {
        var developer = await ds.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();
        
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
        var accountId = Guid.Parse(currentUser.Id);
        if (developer is null || developer.Id != accountId)
            return Forbid();
            
        var project = await projectService.GetProjectAsync(projectId, developer.Id);
        if (project is null)
            return NotFound("Project not found or you don't have access");

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Name and slug are required");

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to create a custom app");

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
}