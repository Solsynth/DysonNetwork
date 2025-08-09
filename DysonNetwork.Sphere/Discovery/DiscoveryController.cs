using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Discovery;

[ApiController]
[Route("/api/discovery")]
public class DiscoveryController(DiscoveryService discoveryService) : ControllerBase
{
    [HttpGet("realms")]
    public Task<List<Realm.Realm>> GetPublicRealms(
        [FromQuery] string? query,
        [FromQuery] int take = 10,
        [FromQuery] int offset = 0
    )
    {
        return discoveryService.GetCommunityRealmAsync(query, take, offset);
    }
}
