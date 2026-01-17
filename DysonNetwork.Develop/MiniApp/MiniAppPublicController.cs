using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.MiniApp;

[ApiController]
[Route("api/miniapps")]
public class MiniAppPublicController(MiniAppService miniAppService, Identity.DeveloperService developerService) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<SnMiniApp>> GetMiniAppBySlug([FromRoute] string slug)
    {
        var miniApp = await miniAppService.GetMiniAppBySlugAsync(slug);
        if (miniApp is null) return NotFound("Mini app not found");

        var developer = await developerService.GetDeveloperById(miniApp.Project.DeveloperId);
        if (developer is null) return NotFound("Developer not found");
        miniApp.Developer = await developerService.LoadDeveloperPublisher(developer);

        return Ok(miniApp);
    }
}
