using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Develop.Models;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.MiniApp;

[ApiController]
[Route("api/miniapps")]
[ApiFeature("developers.miniapps.public", Revision = 1)]
public class MiniAppPublicController(
    MiniAppService miniAppService,
    Identity.DeveloperService developerService,
    MiniAppStorageService storageService
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SnMiniApp>>> DiscoverPlugins(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null)
    {
        take = Math.Clamp(take, 1, 100);
        offset = Math.Max(offset, 0);

        var (miniApps, total) = await miniAppService.GetPublishedMiniAppsAsync(take, offset, search);
        var developers = miniApps.Select(m => m.Project.Developer).ToList();
        await developerService.LoadDeveloperPublisher(developers);
        foreach (var miniApp in miniApps)
        {
            miniApp.Developer = miniApp.Project.Developer;
            ApplyDeveloperAuthor(miniApp);
        }

        Response.Headers.Append("X-Total", total.ToString());
        return Ok(miniApps);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<SnMiniApp>> GetMiniAppBySlug([FromRoute] string slug)
    {
        var miniApp = await miniAppService.GetPublishedMiniAppBySlugAsync(slug);
        if (miniApp is null) return NotFound(new ApiError { Code = "DEV_MINI_APP_NOT_FOUND", Message = "Mini app not found", Status = 404 });

        miniApp.Developer = await developerService.LoadDeveloperPublisher(miniApp.Project.Developer);
        ApplyDeveloperAuthor(miniApp);

        return Ok(miniApp);
    }

    [HttpGet("{slug}/package")]
    public async Task<ActionResult> DownloadPackage([FromRoute] string slug)
    {
        var miniApp = await miniAppService.GetPublishedMiniAppBySlugAsync(slug);
        if (miniApp is null) return NotFound(new ApiError { Code = "DEV_MINI_APP_NOT_FOUND", Message = "Mini app not found", Status = 404 });
        if (string.IsNullOrWhiteSpace(miniApp.PackageStorageKey))
            return NotFound(new ApiError { Code = "DEV_MINI_APP_NO_PACKAGE", Message = "Mini app has no published package", Status = 404 });

        await miniAppService.IncrementDownloadCountAsync(miniApp.Id);

        if (!string.IsNullOrWhiteSpace(miniApp.PackageUrl))
            return Redirect(miniApp.PackageUrl);

        var stream = await storageService.GetPackageStreamAsync(miniApp.PackageStorageKey);
        return File(stream, "application/zip", $"{miniApp.Slug}.zip");
    }

    /// <summary>Public responses credit the owning developer (publisher) as author instead of the manifest author.</summary>
    private static void ApplyDeveloperAuthor(SnMiniApp miniApp)
    {
        if (miniApp.Developer?.Publisher is not { } publisher)
            return;

        miniApp.Author = string.IsNullOrWhiteSpace(publisher.Nick) ? publisher.Name : publisher.Nick;
    }
}
