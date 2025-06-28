using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Connection.WebReader;

[Authorize]
[ApiController]
[Route("/feeds")]
public class WebFeedController(WebFeedService webFeedService, AppDatabase database) : ControllerBase
{
    public class CreateWebFeedRequest
    {
        [Required]
        [MaxLength(8192)]
        public required string Url { get; set; }

        [Required]
        [MaxLength(4096)]
        public required string Title { get; set; }

        [MaxLength(8192)]
        public string? Description { get; set; }
    }

    
    [HttpPost]
    public async Task<IActionResult> CreateWebFeed([FromBody] CreateWebFeedRequest request)
    {
        var feed = await webFeedService.CreateWebFeedAsync(request, User);
        return Ok(feed);
    }
    
    [HttpPost("scrape/{feedId}")]
    [RequiredPermission("maintenance", "web-feeds")]
    public async Task<ActionResult> ScrapeFeed(Guid feedId)
    {
        var feed = await database.Set<WebFeed>().FindAsync(feedId);
        if (feed == null)
        {
            return NotFound();
        }

        await webFeedService.ScrapeFeedAsync(feed);
        return Ok();
    }

    [HttpPost("scrape-all")]
    [RequiredPermission("maintenance", "web-feeds")]
    public async Task<ActionResult> ScrapeAllFeeds()
    {
        var feeds = await database.Set<WebFeed>().ToListAsync();
        foreach (var feed in feeds)
        {
            await webFeedService.ScrapeFeedAsync(feed);
        }

        return Ok();
    }
}
