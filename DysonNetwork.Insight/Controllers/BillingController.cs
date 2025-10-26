using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Insight.Controllers;

[ApiController]
[Route("/api/billing")]
public class BillingController(ThoughtService thoughtService, ILogger<BillingController> logger) : ControllerBase
{
    [HttpPost("settle")]
    [Authorize]
    [RequiredPermission("maintenance", "insight.billing.settle")]
    public async Task<IActionResult> ProcessTokenBilling()
    {
        await thoughtService.SettleThoughtBills(logger);
        return Ok();
    }
}
