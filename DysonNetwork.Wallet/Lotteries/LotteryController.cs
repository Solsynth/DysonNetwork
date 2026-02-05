using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Lotteries;

[ApiController]
[Route("/api/lotteries")]
public class LotteryController(AppDatabase db, LotteryService lotteryService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnLottery>>> GetLotteries(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var lotteries = await lotteryService.GetUserTicketsAsync(currentUser.Id, offset, limit);
        var total = await lotteryService.GetUserTicketCountAsync(currentUser.Id);

        Response.Headers["X-Total"] = total.ToString();

        return Ok(lotteries);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<SnLottery>> GetLottery(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var lottery = await lotteryService.GetTicketAsync(id);
        if (lottery == null || lottery.AccountId != currentUser.Id)
            return NotFound();

        return Ok(lottery);
    }

    [HttpPost("draw")]
    [Authorize]
    [AskPermission("lotteries.draw.perform")]
    public async Task<IActionResult> PerformLotteryDraw()
    {
        await lotteryService.DrawLotteries();
        return Ok();
    }

    [HttpGet("records")]
    public async Task<ActionResult<List<SnLotteryRecord>>> GetLotteryRecords(
        [FromQuery] Instant? startDate = null,
        [FromQuery] Instant? endDate = null,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 20)
    {
        var query = db.LotteryRecords
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(r => r.DrawDate >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(r => r.DrawDate <= endDate.Value);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var records = await query
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return Ok(records);
    }
}
