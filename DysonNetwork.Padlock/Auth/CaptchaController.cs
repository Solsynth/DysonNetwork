using DysonNetwork.Shared.Networking;
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
            return BadRequest(new ApiError { Code = "PADLOCK_CAPTCHA_TOKEN_REQUIRED", Message = "Token is required.", Status = 400 });

        var valid = await auth.ValidateCaptcha(request.Token);
        if (!valid)
            return BadRequest(new ApiError { Code = "PADLOCK_CAPTCHA_INVALID", Message = "Invalid captcha.", Status = 400 });

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
