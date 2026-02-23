using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models = DysonNetwork.Shared.Models;

namespace DysonNetwork.Zone.Publication;

[ApiController]
[Route("/api/sites")]
public class PublicationSiteController(
    PublicationSiteService publicationService,
    RemotePublisherService publisherService
) : ControllerBase
{
    [HttpGet("{pubName}/{slug}")]
    public async Task<ActionResult<SnPublicationSite>> GetSite(string pubName, string slug)
    {
        var site = await publicationService.GetSiteBySlug(slug, pubName);
        if (site == null)
            return NotFound();
        return Ok(site);
    }

    [HttpGet("{pubName}")]
    [Authorize]
    public async Task<ActionResult<List<SnPublicationSite>>> ListSitesForPublisher([FromRoute] string pubName)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null) return NotFound();

        if (!await publisherService.IsMemberWithRole(publisher.Id, accountId, Models.PublisherMemberRole.Viewer))
            return Forbid();

        var sites = await publicationService.GetSitesByPublisherIds([publisher.Id]);
        return Ok(sites);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<SnPublicationSite>>> ListOwnedSites()
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        // list sites for publishers user is member of
        var publishers = await publisherService.GetUserPublishers(accountId);
        var publisherIds = publishers.Select(p => p.Id).ToList();

        var sites = await publicationService.GetSitesByPublisherIds(publisherIds);
        return Ok(sites);
    }

    [HttpPost("{pubName}")]
    [Authorize]
    public async Task<ActionResult<SnPublicationSite>> CreateSite([FromRoute] string pubName,
        [FromBody] PublicationSiteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("slug is required.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required.");

        var site = new SnPublicationSite
        {
            Mode = request.Mode ?? PublicationSiteMode.FullyManaged,
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            PublisherId = publisher.Id,
            Config = request.Config ?? new PublicationSiteConfig(),
            AccountId = accountId
        };
        if (request.Rss != null)
            site.Config.Rss = request.Rss;
        if (request.AutoMinifyAssets.HasValue)
            site.Config.AutoMinifyAssets = request.AutoMinifyAssets.Value;

        try
        {
            site = await publicationService.CreateSite(site, accountId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }

        return Ok(site);
    }

    [HttpPatch("{pubName}/{slug}")]
    [Authorize]
    public async Task<ActionResult<SnPublicationSite>> UpdateSite([FromRoute] string pubName, string slug,
        [FromBody] PublicationSiteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null) return NotFound();

        var site = await publicationService.GetSiteBySlug(slug, pubName);
        if (site == null || site.PublisherId != publisher.Id)
            return NotFound();

        if (request.Mode.HasValue)
            site.Mode = request.Mode.Value;
        if (!string.IsNullOrWhiteSpace(request.Slug))
            site.Slug = request.Slug;
        if (!string.IsNullOrWhiteSpace(request.Name))
            site.Name = request.Name;
        if (request.Description != null)
            site.Description = request.Description;
        if (request.Config != null)
            site.Config = request.Config;
        if (request.Rss != null)
            site.Config.Rss = request.Rss;
        if (request.AutoMinifyAssets.HasValue)
            site.Config.AutoMinifyAssets = request.AutoMinifyAssets.Value;

        try
        {
            site = await publicationService.UpdateSite(site, accountId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return Ok(site);
    }

    [HttpDelete("{pubName}/{slug}")]
    [Authorize]
    public async Task<IActionResult> DeleteSite([FromRoute] string pubName, string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null) return NotFound();

        var site = await publicationService.GetSiteBySlug(slug, pubName);
        if (site == null || site.PublisherId != publisher.Id)
            return NotFound();

        try
        {
            await publicationService.DeleteSite(site.Id, accountId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return NoContent();
    }

    [HttpGet("{pubName}/{siteSlug}/pages")]
    [Authorize]
    public async Task<ActionResult<List<SnPublicationPage>>> ListPagesForSite([FromRoute] string pubName,
        [FromRoute] string siteSlug)
    {
        var site = await publicationService.GetSiteBySlug(siteSlug);
        if (site == null) return NotFound();

        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null || site.PublisherId != publisher.Id) return NotFound();

        var pages = await publicationService.GetPagesForSite(site.Id);
        return Ok(pages);
    }

    [HttpGet("pages/{id:guid}")]
    public async Task<ActionResult<SnPublicationPage>> GetPage(Guid id)
    {
        var page = await publicationService.GetPageById(id);
        if (page == null)
            return NotFound();
        return Ok(page);
    }

    [HttpPost("{pubName}/{siteSlug}/pages")]
    [Authorize]
    public async Task<ActionResult<SnPublicationPage>> CreatePage([FromRoute] string pubName,
        [FromRoute] string siteSlug, [FromBody] PublicationPageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var site = await publicationService.GetSiteBySlug(siteSlug);
        if (site == null) return NotFound();

        var publisher = await publisherService.GetPublisherByName(pubName);
        if (publisher == null || site.PublisherId != publisher.Id) return NotFound();

        var page = new SnPublicationPage
        {
            Type = request.Type,
            Path = request.Path ?? "/",
            Config = request.Config ?? new Dictionary<string, object?>(),
            SiteId = site.Id
        };

        try
        {
            page = await publicationService.CreatePage(page, accountId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return Ok(page);
    }

    [HttpPatch("pages/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnPublicationPage>> UpdatePage(Guid id, [FromBody] PublicationPageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var page = await publicationService.GetPageById(id);
        if (page == null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        page.Type = request.Type;
        if (request.Path != null) page.Path = request.Path;
        if (request.Config != null) page.Config = request.Config;

        try
        {
            page = await publicationService.UpdatePage(page, accountId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return Ok(page);
    }

    [HttpDelete("pages/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePage(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            await publicationService.DeletePage(id, accountId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return NoContent();
    }

    public class PublicationSiteRequest
    {
        public PublicationSiteMode? Mode { get; set; }
        [MaxLength(4096)] public string? Slug { get; set; }
        [MaxLength(4096)] public string? Name { get; set; }
        [MaxLength(8192)] public string? Description { get; set; }
        public PublicationSiteConfig? Config { get; set; }
        public PublicationSiteRssConfig? Rss { get; set; }
        public bool? AutoMinifyAssets { get; set; }
    }

    public class PublicationPageRequest
    {
        public PublicationPageType Type { get; set; }
        [MaxLength(8192)] public string? Path { get; set; }
        public Dictionary<string, object?>? Config { get; set; }
    }
}
