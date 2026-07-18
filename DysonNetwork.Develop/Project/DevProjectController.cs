using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Develop.Project;

[ApiController]
[Route("/api/private/projects")]
[ApiFeature("developers.projects", Revision = 1)]
public class DevProjectController(DevProjectService ps, DeveloperService ds) : ControllerBase
{
    public record DevProjectRequest(
        [MaxLength(1024)] string? Slug,
        [MaxLength(1024)] string? Name,
        [MaxLength(4096)] string? Description
    );

    [HttpGet]
    public async Task<IActionResult> ListProjects([FromQuery(Name = "dev")] string dev)
    {
        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_PROJECT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var projects = await ps.GetProjectsByDeveloperAsync(developer.Id);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProject([FromQuery(Name = "dev")] string dev, Guid id)
    {
        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null) return NotFound(new ApiError { Code = "DEV_PROJECT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        var project = await ps.GetProjectAsync(id, developer.Id);
        if (project is null) return NotFound(new ApiError { Code = "DEV_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        return Ok(project);
    }

    [HttpPost]
    [Authorize]
    [AskPermission(PermissionKeys.DevProjectsCreate)]
    public async Task<IActionResult> CreateProject([FromQuery(Name = "dev")] string dev, [FromBody] DevProjectRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        if (developer is null)
            return NotFound(new ApiError { Code = "DEV_PROJECT_DEVELOPER_NOT_FOUND", Message = "Developer not found", Status = 404 });

        if (!await ds.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), DyPublisherMemberRole.DyEditor))
            return StatusCode(403, ApiError.Unauthorized("You must be an editor of the developer to create a project", forbidden: true));

        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ApiError { Code = "DEV_PROJECT_SLUG_NAME_REQUIRED", Message = "Slug and Name are required", Status = 400 });

        var project = await ps.CreateProjectAsync(developer, request);
        return CreatedAtAction(nameof(GetProject), new { dev, id = project.Id }, project);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.DevProjectsUpdate)]
    public async Task<IActionResult> UpdateProject(
        [FromQuery(Name = "dev")] string dev,
        [FromRoute] Guid id,
        [FromBody] DevProjectRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        var accountId = Guid.Parse(currentUser.Id);

        if (developer is null)
            return StatusCode(403, ApiError.Unauthorized("Developer not found", forbidden: true));
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyManager))
            return StatusCode(403, ApiError.Unauthorized("You must be an manager of the developer to update a project", forbidden: true));

        var project = await ps.UpdateProjectAsync(id, developer.Id, request);
        if (project is null)
            return NotFound(new ApiError { Code = "DEV_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        return Ok(project);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.DevProjectsDelete)]
    public async Task<IActionResult> DeleteProject([FromQuery(Name = "dev")] string dev, [FromRoute] Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var developer = await ds.GetDeveloperByName(dev);
        var accountId = Guid.Parse(currentUser.Id);
        if (developer is null)
            return StatusCode(403, ApiError.Unauthorized("Developer not found", forbidden: true));
        if (!await ds.IsMemberWithRole(developer.PublisherId, accountId, DyPublisherMemberRole.DyManager))
            return StatusCode(403, ApiError.Unauthorized("You must be an manager of the developer to delete a project", forbidden: true));

        var success = await ps.DeleteProjectAsync(id, developer.Id);
        if (!success)
            return NotFound(new ApiError { Code = "DEV_PROJECT_NOT_FOUND", Message = "Project not found", Status = 404 });

        return NoContent();
    }
}
