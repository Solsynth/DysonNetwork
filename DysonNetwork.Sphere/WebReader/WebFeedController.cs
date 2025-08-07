using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.WebReader;

[Authorize]
[ApiController]
[Route("/api/publishers/{pubName}/feeds")]
public class WebFeedController(WebFeedService webFeed, Publisher.PublisherService ps) : ControllerBase
{
    public record WebFeedRequest(
        [MaxLength(8192)] string? Url,
        [MaxLength(4096)] string? Title,
        [MaxLength(8192)] string? Description,
        WebFeedConfig? Config
    );

    [HttpGet]
    public async Task<IActionResult> ListFeeds([FromRoute] string pubName)
    {
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();
        var feeds = await webFeed.GetFeedsByPublisherAsync(publisher.Id);
        return Ok(feeds);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFeed([FromRoute] string pubName, Guid id)
    {
        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var feed = await webFeed.GetFeedAsync(id, publisherId: publisher.Id);
        if (feed == null)
            return NotFound();

        return Ok(feed);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateWebFeed([FromRoute] string pubName, [FromBody] WebFeedRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Url and title are required");

        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to create a web feed");

        var feed = await webFeed.CreateWebFeedAsync(publisher, request);
        return Ok(feed);
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateFeed([FromRoute] string pubName, Guid id, [FromBody] WebFeedRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to update a web feed");

        var feed = await webFeed.GetFeedAsync(id, publisherId: publisher.Id);
        if (feed == null)
            return NotFound();

        feed = await webFeed.UpdateFeedAsync(feed, request);
        return Ok(feed);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteFeed([FromRoute] string pubName, Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to delete a web feed");

        var feed = await webFeed.GetFeedAsync(id, publisherId: publisher.Id);
        if (feed == null)
            return NotFound();

        var result = await webFeed.DeleteFeedAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/scrap")]
    [Authorize]
    public async Task<ActionResult> Scrap([FromRoute] string pubName, Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var publisher = await ps.GetPublisherByName(pubName);
        if (publisher is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ps.IsMemberWithRole(publisher.Id, accountId, Publisher.PublisherMemberRole.Editor))
            return StatusCode(403, "You must be an editor of the publisher to scrape a web feed");

        var feed = await webFeed.GetFeedAsync(id, publisherId: publisher.Id);
        if (feed == null)
        {
            return NotFound();
        }

        await webFeed.ScrapeFeedAsync(feed);
        return Ok();
    }
}
