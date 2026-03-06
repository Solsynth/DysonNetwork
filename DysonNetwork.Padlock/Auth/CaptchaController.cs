using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Padlock.Auth;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("api/v1/auth/captcha")]
[AllowAnonymous]
public class CaptchaController(
    AuthService auth,
    ILogger<CaptchaController> logger
) : ControllerBase
{
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] CaptchaVerifyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token is required" });

        var valid = await auth.ValidateCaptcha(request.Token);
        if (!valid)
            return BadRequest(new { error = "Invalid captcha" });

        return Ok(new { success = true });
    }
}

public record CaptchaVerifyRequest(string Token);
