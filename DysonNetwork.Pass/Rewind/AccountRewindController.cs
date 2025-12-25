using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.Rewind;

[ApiController]
[Route("/api/rewind")]
public class AccountRewindController(AccountRewindService rewindSrv) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SnRewindPoint>> GetCurrentRewindPoint()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var point = await rewindSrv.GetOrCreateRewindPoint(currentUser.Id);
        return Ok(point);
    }
}