using DysonNetwork.Passport.Models;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Rewind;

[ApiController]
[Route("/api/rewind")]
[ApiFeature("rewind", Revision = 1)]
public class AccountRewindController(AccountRewindService rewindSrv) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<ActionResult<SnRewindPoint>> GetRewindPoint([FromRoute] string code)
    {
        var point = await rewindSrv.GetPublicRewindPoint(code);
        if (point is null) return NotFound(new ApiError { Code = "PASSPORT_REWIND_POINT_NOT_FOUND", Message = "Rewind point not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        return Ok(point);
    }
    
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SnRewindPoint>> GetCurrentRewindPoint()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var point = await rewindSrv.GetOrCreateRewindPoint(currentUser.Id);
        return Ok(point);
    }

    [HttpPost("me/{year:int}/public")]
    [Authorize]
    [AskPermission(PermissionKeys.RewindCreate)]
    public async Task<ActionResult<SnRewindPoint>> SetRewindPointPublic([FromRoute] int year)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        try
        {
            var point = await rewindSrv.SetRewindPointPublic(currentUser.Id, year);
            return Ok(point);
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_REWIND_SET_PUBLIC_FAILED", Message = error.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("me/{year:int}/private")]
    [Authorize]
    [AskPermission(PermissionKeys.RewindCreate)]
    public async Task<ActionResult<SnRewindPoint>> SetRewindPointPrivate([FromRoute] int year)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        try
        {
            var point = await rewindSrv.SetRewindPointPrivate(currentUser.Id, year);
            return Ok(point);
        }
        catch (InvalidOperationException error)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_REWIND_SET_PRIVATE_FAILED", Message = error.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }
}