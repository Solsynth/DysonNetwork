using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DysonNetwork.Pass.Auth;

[ApiController]
[Route("/api/captcha")]
public class CaptchaController(
    IConfiguration configuration,
    AuthService authService,
    ILogger<CaptchaController> logger
) : ControllerBase
{
    [HttpGet]
    public IActionResult GetConfiguration()
    {
        return Ok(new
        {
            provider = configuration["Captcha:Provider"],
            apiKey = configuration["Captcha:ApiKey"],
        });
    }

    [HttpPost("verify")]
    [EnableRateLimiting("captcha")]
    public async Task<IActionResult> Verify([FromBody] CaptchaVerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            logger.LogWarning("Captcha verification failed: empty token from {IpAddress}",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return BadRequest("Token is required");
        }

        try
        {
            var isValid = await authService.ValidateCaptcha(request.Token);

            if (!isValid)
            {
                logger.LogWarning("Captcha verification failed: invalid token from {IpAddress}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());
                return BadRequest("Invalid captcha token");
            }

            logger.LogInformation("Captcha verification successful from {IpAddress}",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during captcha verification from {IpAddress}",
                HttpContext.Connection.RemoteIpAddress?.ToString());
            return StatusCode(500, "Internal server error");
        }
    }

    public class CaptchaVerifyRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
