using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.Auth;

[ApiController]
[Route("/api/captcha")]
public class CaptchaController(IConfiguration configuration) : ControllerBase
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
}