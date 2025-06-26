using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Connection.WebReader;

[Authorize]
[ApiController]
[Route("feeds")]
public class WebFeedController(WebFeedService webFeedService) : ControllerBase
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
}
