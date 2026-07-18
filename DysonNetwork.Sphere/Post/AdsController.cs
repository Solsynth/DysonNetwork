using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Sphere.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/ads")]
[ApiFeature("ads", Revision = 1)]
public class AdsController(
    AppDatabase db,
    SponsorService sponsorService
) : ControllerBase
{
    [HttpGet("{name}")]
    public async Task<ActionResult<List<PublicAdvertisingPostStats>>> ListPublisherAds(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var publisher = await db.Publishers.Where(p => p.Name.ToLower() == name.ToLowerInvariant()).FirstOrDefaultAsync();
        if (publisher is null)
            return NotFound();

        var allStats = await sponsorService.ListPublicAdvertisingPostsAsync(publisher.Id);
        var totalCount = allStats.Count;
        var pagedStats = allStats.Skip(offset).Take(take).ToList();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(pagedStats);
    }
}
