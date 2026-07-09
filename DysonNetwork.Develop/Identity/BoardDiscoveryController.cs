using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/apps")]
public class BoardDiscoveryController(
    CustomAppService customApps,
    AppDatabase db,
    DyAuthorizedAppService.DyAuthorizedAppServiceClient boardAuthClient)
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
        [FromQuery] string? slug = null
    )
    {
        string? currentAccountId = ResolveCurrentAccountId();

        // If no user is authenticated, fall back to listing all board-capable apps (public discovery)
        if (string.IsNullOrEmpty(currentAccountId))
        {
            return await DiscoverPublicBoardWidgets(take, offset, slug);
        }

        // Authenticated: return authorized apps' board widgets via Padlock gRPC
        return await DiscoverAuthorizedBoardWidgets(currentAccountId, take, offset, slug);
    }

    private async Task<ActionResult<List<BoardAppDto>>> DiscoverAuthorizedBoardWidgets(
        string accountId, int take, int offset, string? slug)
    {
        var request = new DyQueryAuthorizedBoardAppsRequest
        {
            AccountId = accountId,
            Take = take,
            Offset = offset,
            AppSlug = slug ?? string.Empty
        };

        var grpcResponse = await boardAuthClient.QueryAuthorizedBoardAppsAsync(request);

        var results = new List<BoardAppDto>();
        foreach (var authorizedApp in grpcResponse.Apps)
        {
            var app = await db.CustomApps
                .Include(a => a.Project)
                    .ThenInclude(p => p.Developer)
                .Where(a => a.Id.ToString() == authorizedApp.AppId)
                .FirstOrDefaultAsync();

            if (app is null) continue;

            var publisherName = await ResolvePublisherName(app.Project?.Developer?.PublisherId ?? Guid.Empty, authorizedApp.PublisherName);
            var widgetEntities = await db.BoardWidgets
                .Where(w => w.AppId == app.Id && w.IsEnabled)
                .ToListAsync();
            var widgets = widgetEntities
                .Select(w => w.ToManifest())
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

            if (widgets.Count == 0) continue;

            results.Add(new BoardAppDto(
                Id: app.Id.ToString(),
                Slug: app.Slug,
                Name: app.Name,
                Description: app.Description,
                PublisherName: publisherName,
                BoardWidgets: widgets
            ));
        }

        return Ok(results);
    }

    private async Task<ActionResult<List<BoardAppDto>>> DiscoverPublicBoardWidgets(
        int take, int offset, string? slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var app = await customApps.GetAppBySlugAsync(slug);
            var widgetCount = await db.BoardWidgets.CountAsync(w => w.AppId == app.Id && w.IsEnabled);
            if (widgetCount == 0)
                return Ok(new List<BoardAppDto>());

            return Ok(new List<BoardAppDto> { await MapToDto(app) });
        }

        var apps = await GetBoardCapableAppsWithPublisherAsync(take, offset);
        var dtos = new List<BoardAppDto>();
        foreach (var app in apps)
        {
            dtos.Add(await MapToDto(app));
        }
        return Ok(dtos);
    }

    private async Task<List<SnCustomApp>> GetBoardCapableAppsWithPublisherAsync(int take, int offset)
    {
        var candidates = await db.CustomApps
            .Include(a => a.Project)
                .ThenInclude(p => p.Developer)
            .Where(a => a.Status == CustomAppStatus.Production
                        && a.OauthConfig != null)
            .OrderBy(a => a.Name)
            .ToListAsync();

        var appIdsWithWidgets = await db.BoardWidgets
            .Where(w => w.IsEnabled)
            .Select(w => w.AppId)
            .Distinct()
            .ToListAsync();

        return candidates
            .Where(a => a.OauthConfig!.AllowedScopes.Contains(PermissionKeys.AccountsProfileBoard))
            .Where(a => appIdsWithWidgets.Contains(a.Id))
            .Skip(offset)
            .Take(take)
            .ToList();
    }

    private async Task<BoardAppDto> MapToDto(SnCustomApp app)
    {
        var publisherName = await GetPublisherNameAsync(app);

        var widgetEntities = await db.BoardWidgets
            .Where(w => w.AppId == app.Id && w.IsEnabled)
            .ToListAsync();
        var widgets = widgetEntities
            .Select(w => w.ToManifest())
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
        if (app.Project is null)
            return string.Empty;
        await db.Entry(app.Project).Reference(p => p.Developer).LoadAsync();
        if (app.Project.Developer is null)
            return string.Empty;
        return await ResolvePublisherName(app.Project.Developer.PublisherId, string.Empty);
    }

    private Task<string> ResolvePublisherName(Guid publisherId, string fallback)
    {
        // Publisher name resolution via gRPC; fallback for now
        return Task.FromResult(fallback);
    }

    private string? ResolveCurrentAccountId()
    {
        if (HttpContext.Items.TryGetValue("CurrentUser", out var user) && user is DyAccount account)
            return account.Id;
        return null;
    }
}
