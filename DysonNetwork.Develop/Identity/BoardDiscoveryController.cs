using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/apps")]
public class BoardDiscoveryController(
    CustomAppService customApps,
    AppDatabase db)
    : ControllerBase
{
    public record BoardWidgetDto(
        string Key,
        string Slug,
        bool IsEnabled,
        string RendererType,
        List<SnBoardFieldType> FieldTypes,
        List<string> RequiredFields,
        int? MaxPayloadBytes,
        bool AllowMultiple
    );

    public record BoardAppDto(
        string Id,
        string Slug,
        string Name,
        string? Description,
        string PublisherName,
        List<BoardWidgetDto> BoardWidgets
    );

    [HttpGet("board")]
    public async Task<ActionResult<List<BoardAppDto>>> DiscoverBoardWidgets(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? slug = null)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var app = await customApps.GetAppBySlugAsync(slug);
            if (app is null || app.BoardWidgets is null || app.BoardWidgets.Count == 0)
                return Ok(new List<BoardAppDto>());

            return Ok(new List<BoardAppDto>
            {
                await MapToDto(app)
            });
        }

        var apps = await customApps.GetBoardCapableAppsWithPublisherAsync(take, offset);
        var dtos = new List<BoardAppDto>();
        foreach (var app in apps)
        {
            dtos.Add(await MapToDto(app));
        }
        return Ok(dtos);
    }

    private async Task<BoardAppDto> MapToDto(SnCustomApp app)
    {
        var publisherName = await GetPublisherNameAsync(app);

        var widgets = (app.BoardWidgets ?? new List<SnBoardWidgetManifest>())
            .Where(w => w.IsEnabled)
            .Select(w => new BoardWidgetDto(
                Key: w.Key,
                Slug: w.BuildSlug(publisherName, app.Slug),
                IsEnabled: w.IsEnabled,
                RendererType: w.RendererType,
                FieldTypes: w.FieldTypes,
                RequiredFields: w.RequiredFields,
                MaxPayloadBytes: w.MaxPayloadBytes,
                AllowMultiple: w.AllowMultiple
            ))
            .ToList();

        return new BoardAppDto(
            Id: app.Id.ToString(),
            Slug: app.Slug,
            Name: app.Name,
            Description: app.Description,
            PublisherName: publisherName,
            BoardWidgets: widgets
        );
    }

    private async Task<string> GetPublisherNameAsync(SnCustomApp app)
    {
        await db.Entry(app).Reference(a => a.Project).LoadAsync();
        if (app.Project is null) return string.Empty;
        await db.Entry(app.Project).Reference(p => p.Developer).LoadAsync();
        if (app.Project.Developer is null) return string.Empty;
        await db.Entry(app.Project.Developer).Reference(d => d.Publisher).LoadAsync();
        return app.Project.Developer.Publisher?.Name ?? string.Empty;
    }
}
