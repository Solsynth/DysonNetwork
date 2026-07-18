using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Progression;

[Authorize]
[ApiController]
[Route("/api/accounts/me/progression")]
[ApiFeature("progression.achievements", Revision = 1)]
[ApiFeature("progression.quests", Revision = 1)]
public class ProgressionController(ProgressionService progression) : ControllerBase
{
    [HttpGet("achievements")]
    [ProducesResponseType<List<ProgressionAchievementState>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ProgressionAchievementState>>> GetAchievements([FromQuery] string? query = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (string.IsNullOrWhiteSpace(query))
            return Ok(await progression.ListAchievementStatesAsync(currentUser.Id, HttpContext.RequestAborted));

        return Ok(await progression.SearchAchievementStatesAsync(currentUser.Id, query, HttpContext.RequestAborted));
    }

    [HttpGet("achievements/stats")]
    [ProducesResponseType<ProgressionAchievementStats>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProgressionAchievementStats>> GetAchievementStats()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await progression.GetAchievementStatsAsync(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpGet("quests")]
    [ProducesResponseType<List<ProgressionQuestState>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ProgressionQuestState>>> GetQuests()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await progression.ListQuestStatesAsync(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpGet("grants")]
    [ProducesResponseType<List<SnProgressRewardGrant>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnProgressRewardGrant>>> GetRewardGrants([FromQuery] int take = 50)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        return Ok(await progression.ListRewardGrantsAsync(currentUser.Id, take, HttpContext.RequestAborted));
    }
}
