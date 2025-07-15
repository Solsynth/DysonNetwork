using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway.Controllers;

[ApiController]
[Route("/.well-known")]
public class WellKnownController(IConfiguration configuration, IProxyConfigProvider proxyConfigProvider)
    : ControllerBase
{
    [HttpGet("domains")]
    public IActionResult GetDomainMappings()
    {
        var domainMappings = configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);
        return Ok(domainMappings);
    }

    [HttpGet("routes")]
    public IActionResult GetProxyRules()
    {
        var config = proxyConfigProvider.GetConfig();
        var rules = config.Routes.Select(r => new
        {
            r.RouteId,
            r.ClusterId,
            Match = new
            {
                r.Match.Path,
                Hosts = r.Match.Hosts != null ? string.Join(", ", r.Match.Hosts) : null
            },
            Transforms = r.Transforms?.Select(t => t.Select(kv => $"{kv.Key}: {kv.Value}").ToList())
        }).ToList();

        var clusters = config.Clusters.Select(c => new
        {
            c.ClusterId,
            Destinations = c.Destinations?.Select(d => new
            {
                d.Key,
                d.Value.Address
            }).ToList()
        }).ToList();

        return Ok(new { Rules = rules, Clusters = clusters });
    }
}