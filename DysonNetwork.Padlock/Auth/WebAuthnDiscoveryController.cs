using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[AllowAnonymous]
public class WebAuthnDiscoveryController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("/.well-known/webauthn")]
    [Produces("application/json")]
    public IActionResult GetRelatedOrigins()
    {
        var origins = configuration
            .GetSection("WebAuthn:RelatedOrigins")
            .GetChildren()
            .Select(x => x.Value)
            .OfType<string>()
            .ToArray();

        return Ok(new { origins });
    }
}
