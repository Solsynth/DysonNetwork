using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
