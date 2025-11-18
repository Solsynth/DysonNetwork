using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationPagePresets = DysonNetwork.Shared.Models.PublicationPagePresets;

namespace DysonNetwork.Sphere.Publication;

[ApiController]
[Route("/api/sites")]
public class PublicationSiteController(
    PublicationSiteService publicationService,
    PublisherService publisherService
) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<SnPublicationSite>> GetSite(string slug)
    {
        var site = await publicationService.GetSiteBySlug(slug);
        if (site == null)
            return NotFound();
        return Ok(site);
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

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnPublicationSite>> CreateSite([FromBody] PublicationSiteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var site = new SnPublicationSite
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            PublisherId = request.PublisherId,
            AccountId = accountId
        };

        try
        {
            site = await publicationService.CreateSite(site, accountId);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return Ok(site);
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnPublicationSite>> UpdateSite(Guid id, [FromBody] PublicationSiteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var site = await publicationService.GetSiteById(id);
        if (site == null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        site.Slug = request.Slug;
        site.Name = request.Name;
        site.Description = request.Description ?? site.Description;

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

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteSite(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        try
        {
            await publicationService.DeleteSite(id, accountId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return NoContent();
    }

    [HttpGet("{slug}/page")]
    public async Task<ActionResult<SnPublicationPage>> RenderPage(string slug, [FromQuery] string path = "/")
    {
        var page = await publicationService.RenderPage(slug, path);
        if (page == null)
            return NotFound();
        return Ok(page);
    }

    [HttpGet("{siteId:guid}/pages")]
    [Authorize]
    public async Task<ActionResult<List<SnPublicationPage>>> ListPagesForSite(Guid siteId)
    {
        var pages = await publicationService.GetPagesForSite(siteId);
        return Ok(pages);
    }

    [HttpGet("page/{id:guid}")]
    public async Task<ActionResult<SnPublicationPage>> GetPage(Guid id)
    {
        var page = await publicationService.GetPageById(id);
        if (page == null)
            return NotFound();
        return Ok(page);
    }

    [HttpPost("{siteId:guid}/pages")]
    [Authorize]
    public async Task<ActionResult<SnPublicationPage>> CreatePage(Guid siteId, [FromBody] PublicationPageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var page = new SnPublicationPage
        {
            Preset = request.Preset ?? PublicationPagePresets.Landing,
            Path = request.Path ?? "/",
            Config = request.Config ?? new(),
            SiteId = siteId
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

    [HttpPatch("page/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnPublicationPage>> UpdatePage(Guid id, [FromBody] PublicationPageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var page = await publicationService.GetPageById(id);
        if (page == null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        if (request.Preset != null) page.Preset = request.Preset;
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

    [HttpDelete("page/{id:guid}")]
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
        [MaxLength(4096)] public string Slug { get; set; } = null!;
        [MaxLength(4096)] public string Name { get; set; } = null!;
        [MaxLength(8192)] public string? Description { get; set; }

        public Guid PublisherId { get; set; }
    }

    public class PublicationPageRequest
    {
        [MaxLength(8192)] public string? Preset { get; set; }
        [MaxLength(8192)] public string? Path { get; set; }
        public Dictionary<string, object?>? Config { get; set; }
    }
}
