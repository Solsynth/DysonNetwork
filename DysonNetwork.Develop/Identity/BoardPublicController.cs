using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/apps")]
public class BoardPublicController(
    CustomAppService customApps,
    DeveloperService developers,
    AppDatabase db)
    : ControllerBase
{
    public record BoardWidgetDto(
        string Key,
        string Name,
        string Description,
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
        [FromQuery] string? slug = null
    )
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var app = await customApps.GetAppBySlugAsync(slug);
            if (app is null)
                return Ok(new List<BoardAppDto>());

            var dto = await MapToDto(app);
            if (dto == null)
                return Ok(new List<BoardAppDto>());

            return Ok(new List<BoardAppDto> { dto });
        }

        var apps = await GetBoardCapableAppsWithPublisherAsync(take, offset);
        var dtos = new List<BoardAppDto>();
        foreach (var app in apps)
        {
            var dto = await MapToDto(app);
            if (dto != null)
                dtos.Add(dto);
        }
        return Ok(dtos);
    }

    private async Task<List<SnCustomApp>> GetBoardCapableAppsWithPublisherAsync(int take, int offset)
    {
        var appIdsWithWidgets = await db.BoardWidgets
            .Where(w => w.IsEnabled)
            .Select(w => w.AppId)
            .Distinct()
            .ToListAsync();

        var candidates = await db.CustomApps
            .Include(a => a.Project)
                .ThenInclude(p => p.Developer)
            .Where(a => a.OauthConfig != null
                        && appIdsWithWidgets.Contains(a.Id))
            .OrderBy(a => a.Name)
            .ToListAsync();

        return candidates
            .Where(a => a.OauthConfig!.AllowedScopes.Contains(PermissionKeys.AccountsProfileBoard))
            .Skip(offset)
            .Take(take)
            .ToList();
    }

    private async Task<BoardAppDto?> MapToDto(SnCustomApp app)
    {
        var widgetEntities = await db.BoardWidgets
            .Where(w => w.AppId == app.Id && w.IsEnabled)
            .ToListAsync();

        if (widgetEntities.Count == 0)
            return null;

        var publisherName = await GetPublisherNameAsync(app);

        var widgets = widgetEntities
            .Select(w => w.ToManifest())
            .Select(w => new BoardWidgetDto(
                Key: w.Key,
                Name: w.Name,
                Description: w.Description,
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
        if (app.Project is null)
            return string.Empty;
        await db.Entry(app.Project).Reference(p => p.Developer).LoadAsync();
        if (app.Project.Developer is null)
            return string.Empty;
        // Publishers live in Sphere; hydrate via gRPC (not local EF).
        if (app.Project.Developer.PublisherId == Guid.Empty)
            return string.Empty;
        await developers.LoadDeveloperPublisher(app.Project.Developer);
        return app.Project.Developer.Publisher?.Name ?? string.Empty;
    }
}
