using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("api/apps")]
public class CustomAppPublicController(CustomAppService customAppService, DeveloperService developerService)
    : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<SnCustomApp>> GetCustomAppBySlug([FromRoute] string slug)
    {
        var app = await customAppService.GetAppBySlugAsync(slug);
        if (app is null) return NotFound("Custom app not found");

        var developer = await developerService.GetDeveloperById(app.Project.DeveloperId);
        if (developer is null) return NotFound("Developer not found");
        app.Project.Developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(app);
    }
}

