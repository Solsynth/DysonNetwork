using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Auth;
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
    PaymentService paymentService,
    RemotePublisherService publishers,
    RemoteAccountService accounts
) : ControllerBase
{
    public class UpdateMerchantWalletRequest
    {
        public Guid? WalletId { get; set; }
    }

    /// <summary>
    /// Returns merchant profile including linked payout wallet.
    /// </summary>
    [HttpGet("{merchant}")]
    [Authorize]
    public async Task<IActionResult> GetMerchant([FromRoute] string merchant)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        return Ok(new
        {
            merchantEntity.Id,
            merchantEntity.PublisherId,
            merchantEntity.PaymentWalletId,
            merchantEntity.Name,
            merchantEntity.CreatedAt,
            merchantEntity.UpdatedAt
        });
    }

    /// <summary>
    /// List all settlements for a merchant, newest first.
    /// </summary>
    [HttpGet("{merchant}/settlements")]
    [Authorize]
    public async Task<IActionResult> ListSettlements(
        [FromRoute] string merchant,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        var query = db.MerchantSettlements
            .Where(s => s.MerchantId == merchantEntity.Id)
            .OrderByDescending(s => s.CreatedAt);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var settlements = await query
            .Skip(offset)
            .Take(take)
            .Select(s => new
            {
                s.Id,
                s.MerchantId,
                s.OrderId,
                s.AwardId,
                s.PaymentTransactionId,
                s.PaymentWalletId,
                s.Currency,
                s.Amount,
                s.Status,
                s.SettledBy,
                s.SettledAt,
                s.SettlementTransactionId,
                s.CreatedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        return Ok(settlements);
    }

    /// <summary>
    /// Lists app orders for a merchant by its linked payout wallet, newest first.
    /// </summary>
    [HttpGet("{merchant}/orders")]
    [Authorize]
    public async Task<IActionResult> ListOrders(
        [FromRoute] string merchant,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        if (!merchantEntity.PaymentWalletId.HasValue)
            return BadRequest("No payment wallet configured for this merchant");

        var query = db.PaymentOrders
            .Include(o => o.Items)
            .Where(o => o.PayeeWalletId == merchantEntity.PaymentWalletId.Value && o.AppIdentifier != null)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        var total = await query.CountAsync();
        Response.Headers["X-Total"] = total.ToString();

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(offset)
            .Take(take)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.Currency,
                o.Remarks,
                o.AppIdentifier,
                o.ProductIdentifier,
                o.Meta,
                o.Amount,
                o.ExpiredAt,
                o.PayeeWalletId,
                o.TransactionId,
                o.CreatedAt,
                o.UpdatedAt,
                Items = o.Items.Select(i => new
                {
                    i.Id,
                    i.OrderId,
                    i.ProductIdentifier,
                    i.Quantity,
                    i.UnitPrice,
                    i.Currency,
                    i.CreatedAt,
                    i.UpdatedAt
                })
            })
            .ToListAsync();

        return Ok(orders);
    }

    /// <summary>
    /// Updates the linked payout wallet for a merchant.
    /// Creates the merchant record if it does not exist yet (resolved by publisher name).
    /// </summary>
    [HttpPatch("{merchant}/wallet")]
    [Authorize]
    [AskPermission(PermissionKeys.MerchantsManage)]
    public async Task<IActionResult> UpdateWallet([FromRoute] string merchant, [FromBody] UpdateMerchantWalletRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null)
        {
            // Auto-provision merchant from publisher name so settings can configure payouts
            // before any orders/awards exist.
            if (Guid.TryParse(merchant, out _))
                return NotFound("Merchant not found");

            var publisher = await ResolvePublisherAsync(merchant);
            if (publisher == null)
                return NotFound("Merchant not found");

            if (!Guid.TryParse(currentUser.Id, out var accountId))
                return Unauthorized();

            if (!await publishers.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Owner)
                && !await publishers.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Manager))
                return StatusCode(403, "You do not have access to this merchant");

            if (request.WalletId.HasValue)
            {
                var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == request.WalletId.Value);
                if (wallet == null)
                    return BadRequest("Wallet was not found.");
                if (!await CanManageWalletAsync(currentUser, wallet))
                    return StatusCode(403, "You do not have access to this wallet");
            }

            merchantEntity = await merchantService.UpsertMerchantAsync(
                publisher.Id,
                request.WalletId,
                name: publisher.Name);

            return Ok(new
            {
                merchantEntity.Id,
                merchantEntity.PublisherId,
                merchantEntity.PaymentWalletId,
                merchantEntity.Name
            });
        }

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        if (request.WalletId.HasValue)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == request.WalletId.Value);
            if (wallet == null)
                return BadRequest("Wallet was not found.");
            if (!await CanManageWalletAsync(currentUser, wallet))
                return StatusCode(403, "You do not have access to this wallet");
        }

        merchantEntity.PaymentWalletId = request.WalletId;
        await db.SaveChangesAsync();

        return Ok(new
        {
            merchantEntity.Id,
            merchantEntity.PublisherId,
            merchantEntity.PaymentWalletId,
            merchantEntity.Name
        });
    }

    /// <summary>
    /// Returns pending settlement totals grouped by currency.
    /// </summary>
    [HttpGet("{merchant}/settlements/pending")]
    [Authorize]
    public async Task<IActionResult> GetPendingSummary([FromRoute] string merchant)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        if (!merchantEntity.PaymentWalletId.HasValue)
            return BadRequest("No payment wallet configured for this merchant");

        var totals = await merchantService.GetPendingTotalsAsync(merchantEntity.PaymentWalletId.Value);

        return Ok(new
        {
            MerchantId = merchantEntity.Id,
            WalletId = merchantEntity.PaymentWalletId,
            Pending = totals.ToDictionary(
                kv => kv.Key,
                kv => new { Count = kv.Value.Count, Total = kv.Value.Total })
        });
    }

    /// <summary>
    /// Manually settle all pending settlements for a merchant.
    /// Only the wallet owner may trigger this.
    /// </summary>
    [HttpPost("{merchant}/settlements/settle")]
    [Authorize]
    [AskPermission(PermissionKeys.MerchantsSettlementsManage)]
    public async Task<IActionResult> ManualSettle([FromRoute] string merchant)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have permission to settle this merchant");

        if (!merchantEntity.PaymentWalletId.HasValue)
            return BadRequest("No payment wallet configured for this merchant");

        var transactions = await merchantService.SettleWalletAsync(
            merchantEntity.PaymentWalletId.Value,
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
    [HttpGet("{merchant}/stats/overview")]
    [Authorize]
    public async Task<IActionResult> GetOverviewStats([FromRoute] string merchant)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        var stats = await merchantService.GetOverviewStatsAsync(merchantEntity.Id);

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
    [HttpGet("{merchant}/stats/incoming")]
    [Authorize]
    public async Task<IActionResult> GetPeriodIncoming(
        [FromRoute] string merchant,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? currency = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        var fromInstant = from.HasValue
            ? Instant.FromDateTimeUtc(from.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.AddMonths(-1).ToUniversalTime());
        var toInstant = to.HasValue
            ? Instant.FromDateTimeUtc(to.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.ToUniversalTime());

        var result = await merchantService.GetPeriodIncomingAsync(
            merchantEntity.Id, fromInstant, toInstant, currency);

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
    [HttpGet("{merchant}/stats/daily")]
    [Authorize]
    public async Task<IActionResult> GetDailyIncoming(
        [FromRoute] string merchant,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? currency = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var merchantEntity = await GetMerchantAsync(merchant);
        if (merchantEntity == null) return NotFound("Merchant not found");

        if (!await IsMerchantOwner(currentUser, merchantEntity))
            return StatusCode(403, "You do not have access to this merchant");

        var fromInstant = from.HasValue
            ? Instant.FromDateTimeUtc(from.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.AddMonths(-1).ToUniversalTime());
        var toInstant = to.HasValue
            ? Instant.FromDateTimeUtc(to.Value.ToUniversalTime())
            : Instant.FromDateTimeUtc(DateTime.UtcNow.ToUniversalTime());

        var result = await merchantService.GetDailyIncomingAsync(
            merchantEntity.Id, fromInstant, toInstant, currency);

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

    private async Task<SnMerchant?> GetMerchantAsync(string merchant)
    {
        if (Guid.TryParse(merchant, out var merchantId))
            return await db.Merchants.FirstOrDefaultAsync(m => m.Id == merchantId);

        var publisher = await ResolvePublisherAsync(merchant);
        if (publisher != null)
            return await db.Merchants.FirstOrDefaultAsync(m => m.PublisherId == publisher.Id);

        return await db.Merchants.FirstOrDefaultAsync(m => m.Name == merchant);
    }

    private async Task<SnPublisher?> ResolvePublisherAsync(string value)
    {
        var publisher = await publishers.GetPublisherByName(value);
        if (publisher != null)
            return publisher;

        var account = (await accounts.SearchAccounts(value))
            .FirstOrDefault(a => string.Equals(a.Name, value, StringComparison.OrdinalIgnoreCase));
        if (account == null || !Guid.TryParse(account.Id, out var accountId))
            return null;

        return (await publishers.GetUserPublishers(accountId))
            .FirstOrDefault(p => p.Type == PublisherType.Individual);
    }

    private async Task<bool> CanManageWalletAsync(DyAccount currentUser, SnWallet wallet)
    {
        if (!Guid.TryParse(currentUser.Id, out var accountId))
            return false;

        if (wallet.AccountId == accountId)
            return true;

        if (!wallet.RealmId.HasValue)
            return false;

        var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
        if (publisher == null)
            return false;

        return await publishers.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor);
    }

    /// <summary>
    /// Returns true if the current account owns the wallet configured for this merchant.
    /// </summary>
    private async Task<bool> IsMerchantOwner(DyAccount currentUser, SnMerchant merchant)
    {
        if (!Guid.TryParse(currentUser.Id, out var accountId))
            return false;

        if (await publishers.IsMemberWithRole(merchant.PublisherId, accountId, PublisherMemberRole.Owner))
            return true;
        if (await publishers.IsMemberWithRole(merchant.PublisherId, accountId, PublisherMemberRole.Manager))
            return true;

        if (!merchant.PaymentWalletId.HasValue)
            return false;

        var wallet = await db.Wallets
            .FirstOrDefaultAsync(w => w.Id == merchant.PaymentWalletId.Value);

        return wallet?.AccountId == accountId;
    }
}
