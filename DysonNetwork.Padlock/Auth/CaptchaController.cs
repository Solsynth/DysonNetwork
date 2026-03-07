using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Padlock.Auth;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("api/auth/captcha")]
[AllowAnonymous]
public class CaptchaController(
    AuthService auth,
    IConfiguration configuration
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
    
    [HttpGet]
    public IActionResult GetConfiguration()
    {
        return Ok(new
        {
            provider = configuration["Captcha:Provider"],
            apiKey = configuration["Captcha:ApiKey"],
        });
    }
}

public record CaptchaVerifyRequest(string Token);
