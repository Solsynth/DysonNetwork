using DysonNetwork.Insight.Thought;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Insight.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController(AppDatabase db, ThoughtService thoughtService, ILogger<BillingController> logger)
    : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetBillingStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var isMarked = await db.UnpaidAccounts.AnyAsync(u => u.AccountId == accountId);
        return Ok(isMarked ? new { status = "unpaid" } : new { status = "ok" });
    }

    [HttpPost("retry")]
    public async Task<IActionResult> RetryBilling()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var (success, cost) = await thoughtService.RetryBillingForAccountAsync(accountId, logger);

        if (success)
        {
            return Ok(cost > 0
                ? new { message = $"Billing retry successful. Billed {cost} points." }
                : new { message = "No outstanding payment found." });
        }

        return BadRequest(new { message = "Billing retry failed. Please check your balance and try again." });
    }
}