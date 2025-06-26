using System.Collections.Generic;
using System.Threading.Tasks;
using DysonNetwork.Sphere.Realm;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Discovery;

[ApiController]
[Route("discovery")]
public class DiscoveryController(DiscoveryService discoveryService) : ControllerBase
{
    [HttpGet("realms")]
    public Task<List<Realm.Realm>> GetPublicRealms([FromQuery] string? query, [FromQuery] List<string>? tags)
    {
        return discoveryService.GetPublicRealmsAsync(query, tags);
    }
}