using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[AllowAnonymous]
public class WebAuthnDiscoveryController(IConfiguration configuration) : ControllerBase
{
    public record WebAuthnConfigurationResponse(string RpId, string RpName);

    [HttpGet("/api/auth/webauthn/config")]
    public ActionResult<WebAuthnConfigurationResponse> GetConfiguration()
    {
        var rpId = configuration["WebAuthn:RpId"] ?? HttpContext.Request.Host.Host;
        var rpName = configuration["WebAuthn:RpName"] ?? "Solar Network";
        return Ok(new WebAuthnConfigurationResponse(rpId, rpName));
    }

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
