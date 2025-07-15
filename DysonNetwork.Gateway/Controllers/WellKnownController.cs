using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Gateway.Controllers;

[ApiController]
[Route("/.well-known")]
public class WellKnownController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("domains")]
    public IActionResult GetDomainMappings()
    {
        var domainMappings = configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);
        return Ok(domainMappings);
    }
}