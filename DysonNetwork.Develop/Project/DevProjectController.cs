using System.ComponentModel.DataAnnotations;
using DysonNetwork.Develop.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Develop.Project;

[ApiController]
[Route("/api/developers/{pubName}/projects")]
public class DevProjectController(DevProjectService projectService, DeveloperService developerService) : ControllerBase
{
    public record DevProjectRequest(
        [MaxLength(1024)] string? Slug,
        [MaxLength(1024)] string? Name,
        [MaxLength(4096)] string? Description
    );

    [HttpGet]
    public async Task<IActionResult> ListProjects([FromRoute] string pubName)
    {
        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();
        
        var projects = await projectService.GetProjectsByDeveloperAsync(developer.Id);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProject([FromRoute] string pubName, Guid id)
    {
        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null) return NotFound();

        var project = await projectService.GetProjectAsync(id, developer.Id);
        if (project is null) return NotFound();

        return Ok(project);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateProject([FromRoute] string pubName, [FromBody] DevProjectRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) 
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        if (developer is null)
            return NotFound("Developer not found");
            
        if (!await developerService.IsMemberWithRole(developer.PublisherId, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the developer to create a project");

        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Slug and Name are required");

        var project = await projectService.CreateProjectAsync(developer, request);
        return CreatedAtAction(
            nameof(GetProject), 
            new { pubName, id = project.Id },
            project
        );
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateProject(
        [FromRoute] string pubName, 
        [FromRoute] Guid id,
        [FromBody] DevProjectRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) 
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        var accountId = Guid.Parse(currentUser.Id);
        if (developer is null || developer.Id != accountId)
            return Forbid();

        var project = await projectService.UpdateProjectAsync(id, developer.Id, request);
        if (project is null)
            return NotFound();

        return Ok(project);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteProject([FromRoute] string pubName, [FromRoute] Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) 
            return Unauthorized();

        var developer = await developerService.GetDeveloperByName(pubName);
        var accountId = Guid.Parse(currentUser.Id);
        if (developer is null || developer.Id != accountId)
            return Forbid();

        var success = await projectService.DeleteProjectAsync(id, developer.Id);
        if (!success)
            return NotFound();

        return NoContent();
    }
}
