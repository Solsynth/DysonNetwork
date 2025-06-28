using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Developer;

[ApiController]
[Route("/developers/apps")]
public class CustomAppController(CustomAppService customAppService, PublisherService ps) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> GetApps([FromQuery] Guid publisherId)
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

    [HttpPost("")]
    public async Task<IActionResult> CreateApp([FromBody] CreateAppDto dto)
    {
        var app = await customAppService.CreateAppAsync(dto.PublisherId, dto.Name, dto.Slug);
        if (app == null)
        {
            return BadRequest("Invalid publisher ID or missing developer feature flag");
        }
        return CreatedAtAction(nameof(GetApp), new { id = app.Id }, app);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateApp(Guid id, [FromBody] UpdateAppDto dto)
    {
        var app = await customAppService.UpdateAppAsync(id, dto.Name, dto.Slug);
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

public record CreateAppDto(Guid PublisherId, string Name, string Slug);
public record UpdateAppDto(string Name, string Slug);