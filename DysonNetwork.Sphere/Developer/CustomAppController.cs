using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Developer;

[ApiController]
[Route("/developers/apps")]
public class CustomAppController(CustomAppService customAppService, PublisherService ps) : ControllerBase
{
    public record CreateAppRequest(Guid PublisherId, string Name, string Slug);
    public record UpdateAppRequest(string Name, string Slug);
    
    [HttpGet]
    public async Task<IActionResult> ListApps([FromQuery] Guid publisherId)
    {
        var apps = await customAppService.GetAppsByPublisherAsync(publisherId);
        return Ok(apps);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetApp(Guid id)
    {
        var app = await customAppService.GetAppAsync(id);
        if (app == null)
        { 
            return NotFound();
        }
        return Ok(app);
    }

    [HttpPost]
    public async Task<IActionResult> CreateApp([FromBody] CreateAppRequest request)
    {
        var app = await customAppService.CreateAppAsync(request.PublisherId, request.Name, request.Slug);
        if (app == null)
        {
            return BadRequest("Invalid publisher ID or missing developer feature flag");
        }
        return CreatedAtAction(nameof(GetApp), new { id = app.Id }, app);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateApp(Guid id, [FromBody] UpdateAppRequest request)
    {
        var app = await customAppService.UpdateAppAsync(id, request.Name, request.Slug);
        if (app == null)
        {
            return NotFound();
        }
        return Ok(app);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteApp(Guid id)
    {
        var result = await customAppService.DeleteAppAsync(id);
        if (!result)
        {
            return NotFound();
        }
        return NoContent();
    }
}
