using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Progression;

[Authorize]
[ApiController]
[Route("/api/accounts/me/progression")]
public class ProgressionController(ProgressionService progression) : ControllerBase
{
    [HttpGet("achievements")]
    [ProducesResponseType<List<ProgressionAchievementState>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ProgressionAchievementState>>> GetAchievements()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        return Ok(await progression.ListAchievementStatesAsync(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpGet("quests")]
    [ProducesResponseType<List<ProgressionQuestState>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ProgressionQuestState>>> GetQuests()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        return Ok(await progression.ListQuestStatesAsync(currentUser.Id, HttpContext.RequestAborted));
    }

    [HttpGet("grants")]
    [ProducesResponseType<List<SnProgressRewardGrant>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnProgressRewardGrant>>> GetRewardGrants([FromQuery] int take = 50)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        return Ok(await progression.ListRewardGrantsAsync(currentUser.Id, take, HttpContext.RequestAborted));
    }
}
