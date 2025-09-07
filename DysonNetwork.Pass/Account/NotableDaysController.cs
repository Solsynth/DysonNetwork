using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/notable")]
public class NotableDaysController(NotableDaysService days) : ControllerBase
{
    [HttpGet("{regionCode}/{year:int}")]
    public async Task<ActionResult<List<NotableDay>>> GetRegionDays(string regionCode, int year)
    {
        var result = await days.GetNotableDays(year, regionCode);
        return Ok(result);
    }

    [HttpGet("{regionCode}")]
    public async Task<ActionResult<List<NotableDay>>> GetRegionDaysCurrentYear(string regionCode)
    {
        var currentYear = DateTime.Now.Year;
        var result = await days.GetNotableDays(currentYear, regionCode);
        return Ok(result);
    }

    [HttpGet("me/{year:int}")]
    [Authorize]
    public async Task<ActionResult<List<NotableDay>>> GetAccountNotableDays(int year)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var region = currentUser.Region;
        if (string.IsNullOrWhiteSpace(region)) region = "us";

        var result = await days.GetNotableDays(year, region);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<NotableDay>>> GetAccountNotableDaysCurrentYear()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var currentYear = DateTime.Now.Year;
        var region = currentUser.Region;
        if (string.IsNullOrWhiteSpace(region)) region = "us";

        var result = await days.GetNotableDays(currentYear, region);
        return Ok(result);
    }
}
