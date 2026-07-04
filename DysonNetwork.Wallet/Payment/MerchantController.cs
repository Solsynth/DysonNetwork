using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/merchants")]
public class MerchantController(
    AppDatabase db,
    MerchantService merchantService,
    WalletService walletService,
    PaymentService paymentService
) : ControllerBase
{
    /// <summary>
    /// List all settlements for a merchant, newest first.
    /// </summary>
    [HttpGet("{merchantId:guid}/settlements")]
    [Authorize]
    public async Task<IActionResult> ListSettlements(
        [FromRoute] Guid merchantId,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have access to this merchant");

        var query = db.MerchantSettlements
            .Where(s => s.MerchantId == merchantId)
            .OrderByDescending(s => s.CreatedAt);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var settlements = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(settlements);
    }

    /// <summary>
    /// Returns pending settlement totals grouped by currency.
    /// </summary>
    [HttpGet("{merchantId:guid}/settlements/pending")]
    [Authorize]
    public async Task<IActionResult> GetPendingSummary([FromRoute] Guid merchantId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have access to this merchant");

        if (!merchant.PaymentWalletId.HasValue)
            return BadRequest("No payment wallet configured for this merchant");

        var totals = await merchantService.GetPendingTotalsAsync(merchant.PaymentWalletId.Value);

        return Ok(new
        {
            MerchantId = merchantId,
            WalletId = merchant.PaymentWalletId,
            Pending = totals.ToDictionary(
                kv => kv.Key,
                kv => new { Count = kv.Value.Count, Total = kv.Value.Total })
        });
    }

    /// <summary>
    /// Manually settle all pending settlements for a merchant.
    /// Only the wallet owner may trigger this.
    /// </summary>
    [HttpPost("{merchantId:guid}/settlements/settle")]
    [Authorize]
    public async Task<IActionResult> ManualSettle([FromRoute] Guid merchantId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have permission to settle this merchant");

        if (!merchant.PaymentWalletId.HasValue)
            return BadRequest("No payment wallet configured for this merchant");

        var transactions = await merchantService.SettleWalletAsync(
            merchant.PaymentWalletId.Value,
            MerchantSettlementTrigger.Manual,
            walletService,
            paymentService);

        if (transactions.Count == 0)
            return Ok(new { Message = "No pending funds to settle" });

        return Ok(new
        {
            Message = $"Settled {transactions.Count} currency group(s)",
            Transactions = transactions.Select(t => new { t.Id, t.Currency, t.Amount })
        });
    }

    /// <summary>
    /// Returns overview stats for a merchant: total pending, settled, this-month, by-currency breakdowns.
    /// </summary>
    [HttpGet("{merchantId:guid}/stats/overview")]
    [Authorize]
    public async Task<IActionResult> GetOverviewStats([FromRoute] Guid merchantId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have access to this merchant");

        var stats = await merchantService.GetOverviewStatsAsync(merchantId);

        return Ok(new
        {
            stats.TotalPending,
            stats.TotalSettled,
            stats.TotalAllTime,
            Pending = stats.PendingByCurrency.ToDictionary(kv => kv.Key, kv => new { kv.Value.Count, kv.Value.Total }),
            Settled = stats.SettledByCurrency.ToDictionary(kv => kv.Key, kv => new { kv.Value.Count, kv.Value.Total }),
            ThisMonth = stats.ThisMonthByCurrency.ToDictionary(kv => kv.Key, kv => new { kv.Value.Count, kv.Value.Total })
        });
    }

    /// <summary>
    /// Returns incoming settlements within a date range, grouped by status and currency.
    /// Query: /stats/incoming?from=2026-01-01&to=2026-01-31&currency=points
    /// </summary>
    [HttpGet("{merchantId:guid}/stats/incoming")]
    [Authorize]
    public async Task<IActionResult> GetPeriodIncoming(
        [FromRoute] Guid merchantId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? currency = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have access to this merchant");

        var fromInstant = from.HasValue
            ? Instant.FromDateTimeUtc(from.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.AddMonths(-1).ToUniversalTime());
        var toInstant = to.HasValue
            ? Instant.FromDateTimeUtc(to.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.ToUniversalTime());

        var result = await merchantService.GetPeriodIncomingAsync(
            merchantId, fromInstant, toInstant, currency);

        return Ok(new
        {
            result.From,
            result.To,
            result.Currency,
            result.TotalCount,
            result.TotalAmount,
            ByStatus = result.ByStatus.ToDictionary(kv => kv.Key.ToString(), kv => new { kv.Value.Count, kv.Value.Total }),
            ByCurrency = result.ByCurrency.ToDictionary(kv => kv.Key, kv => new { kv.Value.Count, kv.Value.Total }),
            Settlements = result.Settlements.Select(s => new
            {
                s.Id,
                s.OrderId,
                s.AwardId,
                s.Currency,
                s.Amount,
                s.Status,
                s.CreatedAt,
                s.SettledAt,
                s.SettledBy
            })
        });
    }

    /// <summary>
    /// Returns daily incoming totals for charting. Optional currency filter.
    /// Query: /stats/daily?from=2026-01-01&to=2026-01-31&currency=points
    /// </summary>
    [HttpGet("{merchantId:guid}/stats/daily")]
    [Authorize]
    public async Task<IActionResult> GetDailyIncoming(
        [FromRoute] Guid merchantId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? currency = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchant = await db.Merchants.FindAsync(merchantId);
        if (merchant == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchant))
            return StatusCode(403, "You do not have access to this merchant");

        var fromInstant = from.HasValue
            ? Instant.FromDateTimeUtc(from.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.AddMonths(-1).ToUniversalTime());
        var toInstant = to.HasValue
            ? Instant.FromDateTimeUtc(to.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.ToUniversalTime());

        var result = await merchantService.GetDailyIncomingAsync(
            merchantId, fromInstant, toInstant, currency);

        return Ok(new
        {
            From = fromInstant,
            To = toInstant,
            Currency = currency,
            Daily = result.Select(d => new
            {
                Date = d.Date.ToString("yyyy-MM-dd", null),
                d.Count,
                d.Total,
                d.ByCurrency
            })
        });
    }

    /// <summary>
    /// Returns true if the current account owns the wallet configured for this merchant.
    /// </summary>
    private async Task<bool> IsMerchantOwner(DyAccount currentUser, SnMerchant merchant)
    {
        if (!merchant.PaymentWalletId.HasValue)
            return false;

        var wallet = await db.Wallets
            .FirstOrDefaultAsync(w => w.Id == merchant.PaymentWalletId.Value);

        // ponytail: wallet owner = merchant owner. For realm wallets, realm members check is deferred
        return wallet?.AccountId == Guid.Parse(currentUser.Id);
    }
}
