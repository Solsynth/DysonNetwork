using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("config")]
public class ConfigurationController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(configuration.GetSection("Client").Get<Dictionary<string, object>>());

    [HttpGet("site")]
    public IActionResult GetSiteUrl() => Ok(configuration["SiteUrl"]);
}