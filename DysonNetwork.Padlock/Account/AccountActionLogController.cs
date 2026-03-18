using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Account;

[Authorize]
[RequireInteractiveSession]
[ApiController]
[Route("/api/actions")]
public class AccountActionLogController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SnActionLog>>> GetActionLogs(
        [FromQuery] string? action,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        take = take <= 0 ? 50 : Math.Min(take, 1000);
        offset = Math.Max(offset, 0);

        var query = db.ActionLogs.AsNoTracking().Where(log => log.AccountId == currentUser.Id);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(log => log.Action == action);

        query = query.OrderByDescending(log => log.CreatedAt);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var logs = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(logs);
    }
}
