using DysonNetwork.Passport.Models;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Rewind;

[ApiController]
[Route("/api/rewind")]
public class AccountRewindController(AccountRewindService rewindSrv) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<ActionResult<SnRewindPoint>> GetRewindPoint([FromRoute] string code)
    {
        var point = await rewindSrv.GetPublicRewindPoint(code);
        if (point is null) return NotFound();
        return Ok(point);
    }
    
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SnRewindPoint>> GetCurrentRewindPoint()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var point = await rewindSrv.GetOrCreateRewindPoint(currentUser.Id);
        return Ok(point);
    }

    [HttpPost("me/{year:int}/public")]
    [Authorize]
    [AskPermission(PermissionKeys.RewindCreate)]
    public async Task<ActionResult<SnRewindPoint>> SetRewindPointPublic([FromRoute] int year)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        try
        {
            var point = await rewindSrv.SetRewindPointPublic(currentUser.Id, year);
            return Ok(point);
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(error.Message);
        }
    }

    [HttpPost("me/{year:int}/private")]
    [Authorize]
    [AskPermission(PermissionKeys.RewindCreate)]
    public async Task<ActionResult<SnRewindPoint>> SetRewindPointPrivate([FromRoute] int year)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        try
        {
            var point = await rewindSrv.SetRewindPointPrivate(currentUser.Id, year);
            return Ok(point);
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(error.Message);
        }
    }
}