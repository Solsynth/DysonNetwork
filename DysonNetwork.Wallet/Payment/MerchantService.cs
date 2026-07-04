using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

/// <summary>
/// Manages merchant registration and settlement lifecycle.
/// Called by PaymentService (app orders), PostService (publisher awards),
/// and the daily settlement job.
/// </summary>
public class MerchantService(AppDatabase db)
{
    public async Task<SnMerchant> UpsertMerchantAsync(
        Guid publisherId,
        Guid? paymentWalletId,
        string? name = null)
    {
        var merchant = await db.Merchants
            .FirstOrDefaultAsync(m => m.PublisherId == publisherId);

        if (merchant == null)
        {
            merchant = new SnMerchant
            {
                PublisherId = publisherId,
                PaymentWalletId = paymentWalletId,
                Name = name
            };
            db.Merchants.Add(merchant);
        }
        else
        {
            merchant.PaymentWalletId = paymentWalletId;
            if (name != null) merchant.Name = name;
        }

        await db.SaveChangesAsync();
        return merchant;
    }

    public async Task<SnMerchant?> GetMerchantByPublisherAsync(Guid publisherId)
    {
        return await db.Merchants
            .FirstOrDefaultAsync(m => m.PublisherId == publisherId);
    }

    public async Task<SnMerchant?> GetMerchantByIdAsync(Guid merchantId)
    {
        return await db.Merchants
            .FirstOrDefaultAsync(m => m.Id == merchantId);
    }

    /// <summary>
    /// Creates a settlement record for an app order payment (funds held in escrow).
    /// </summary>
    public async Task<SnMerchantSettlement> CreateSettlementAsync(
        Guid merchantId,
        Guid? paymentTransactionId,
        Guid paymentWalletId,
        string currency,
        decimal amount,
        Guid? orderId = null,
        Guid? awardId = null)
    {
        var settlement = new SnMerchantSettlement
        {
            MerchantId = merchantId,
            OrderId = orderId,
            AwardId = awardId,
            PaymentTransactionId = paymentTransactionId,
            PaymentWalletId = paymentWalletId,
            Currency = currency,
            Amount = amount,
            Status = MerchantSettlementStatus.Pending
        };

        db.MerchantSettlements.Add(settlement);
        await db.SaveChangesAsync();
        return settlement;
    }

    /// <summary>
    /// Returns pending settlements grouped by wallet + currency.
    /// </summary>
    public async Task<Dictionary<Guid, List<SnMerchantSettlement>>> GetPendingSettlementsGroupedAsync()
    {
        var pending = await db.MerchantSettlements
            .Include(s => s.PaymentTransaction)
            .Where(s => s.Status == MerchantSettlementStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        return pending
            .GroupBy(s => s.PaymentWalletId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Returns pending settlements for a single wallet, grouped by currency.
    /// </summary>
    public async Task<List<SnMerchantSettlement>> GetPendingSettlementsByWalletAsync(Guid paymentWalletId)
    {
        return await db.MerchantSettlements
            .Include(s => s.PaymentTransaction)
            .Where(s => s.Status == MerchantSettlementStatus.Pending
                     && s.PaymentWalletId == paymentWalletId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Return pending totals for a wallet, grouped by currency.
    /// </summary>
    public async Task<Dictionary<string, (int Count, decimal Total)>> GetPendingTotalsAsync(Guid paymentWalletId)
    {
        var settlements = await db.MerchantSettlements
            .Where(s => s.Status == MerchantSettlementStatus.Pending
                     && s.PaymentWalletId == paymentWalletId)
            .GroupBy(s => s.Currency)
            .Select(g => new { Currency = g.Key, Count = g.Count(), Total = g.Sum(s => s.Amount) })
            .ToListAsync();

        return settlements.ToDictionary(s => s.Currency, s => (s.Count, s.Total));
    }

    /// <summary>
    /// Settles all pending settlements for a wallet. Groups by currency to minimize
    /// transaction count. Returns the settlement transaction for the largest group.
    /// Called by both the daily job and manual settlement API.
    /// </summary>
    public async Task<List<SnWalletTransaction>> SettleWalletAsync(
        Guid paymentWalletId,
        MerchantSettlementTrigger trigger,
        WalletService walletService,
        PaymentService paymentService)
    {
        var pending = await GetPendingSettlementsByWalletAsync(paymentWalletId);
        if (pending.Count == 0)
            return [];

        var byCurrency = pending.GroupBy(s => s.Currency);
        var results = new List<SnWalletTransaction>();
        var now = SystemClock.Instance.GetCurrentInstant();

        foreach (var group in byCurrency)
        {
            var total = group.Sum(s => s.Amount);

            // System credit → payee wallet (no payer = system)
            var settlementTx = await paymentService.CreateTransactionAsync(
                payerWalletId: null,
                payeeWalletId: paymentWalletId,
                currency: group.Key,
                amount: total,
                remarks: $"Settlement: {group.Count()} transactions",
                type: TransactionType.System
            );

            foreach (var settlement in group)
            {
                settlement.Status = MerchantSettlementStatus.Settled;
                settlement.SettledBy = trigger;
                settlement.SettledAt = now;
                settlement.SettlementTransactionId = settlementTx.Id;
            }

            results.Add(settlementTx);
        }

        await db.SaveChangesAsync();
        return results;
    }

    /// <summary>
    /// Marks a single settlement as cancelled (e.g., order refund before settlement).
    /// </summary>
    public async Task CancelSettlementAsync(Guid settlementId)
    {
        var settlement = await db.MerchantSettlements.FindAsync(settlementId);
        if (settlement == null || settlement.Status != MerchantSettlementStatus.Pending)
            return;

        settlement.Status = MerchantSettlementStatus.Cancelled;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cancels settlements linked to a specific order (e.g., order removed).
    /// </summary>
    public async Task CancelSettlementsByOrderAsync(Guid orderId)
    {
        await db.MerchantSettlements
            .Where(s => s.OrderId == orderId && s.Status == MerchantSettlementStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, MerchantSettlementStatus.Cancelled));
    }

    /// <summary>
    /// Returns overview stats for a merchant: total pending, total settled, total all-time.
    /// </summary>
    public async Task<MerchantOverviewStats> GetOverviewStatsAsync(Guid merchantId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var nowDt = now.InUtc();
        var startOfMonth = Instant.FromDateTimeUtc(new DateTime(nowDt.Year, nowDt.Month, 1, 0, 0, 0, DateTimeKind.Utc));

        var settlements = await db.MerchantSettlements
            .Where(s => s.MerchantId == merchantId)
            .ToListAsync();

        var pending = settlements.Where(s => s.Status == MerchantSettlementStatus.Pending).ToList();
        var settled = settlements.Where(s => s.Status == MerchantSettlementStatus.Settled).ToList();
        var thisMonth = settlements.Where(s => s.CreatedAt >= startOfMonth).ToList();

        return new MerchantOverviewStats
        {
            TotalPending = pending.Count,
            TotalSettled = settled.Count,
            TotalAllTime = settlements.Count,
            PendingByCurrency = pending
                .GroupBy(s => s.Currency)
                .ToDictionary(g => g.Key, g => new CurrencyStat { Count = g.Count(), Total = g.Sum(s => s.Amount) }),
            SettledByCurrency = settled
                .GroupBy(s => s.Currency)
                .ToDictionary(g => g.Key, g => new CurrencyStat { Count = g.Count(), Total = g.Sum(s => s.Amount) }),
            ThisMonthByCurrency = thisMonth
                .GroupBy(s => s.Currency)
                .ToDictionary(g => g.Key, g => new CurrencyStat { Count = g.Count(), Total = g.Sum(s => s.Amount) })
        };
    }

    /// <summary>
    /// Returns settlements created within a date range, grouped by currency and status.
    /// Useful for period incoming reports.
    /// </summary>
    public async Task<PeriodIncomingResult> GetPeriodIncomingAsync(
        Guid merchantId,
        Instant from,
        Instant to,
        string? currency = null)
    {
        var query = db.MerchantSettlements
            .Where(s => s.MerchantId == merchantId && s.CreatedAt >= from && s.CreatedAt <= to);

        if (!string.IsNullOrEmpty(currency))
            query = query.Where(s => s.Currency == currency);

        var settlements = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();

        return new PeriodIncomingResult
        {
            From = from,
            To = to,
            Currency = currency,
            TotalCount = settlements.Count,
            TotalAmount = settlements.Sum(s => s.Amount),
            ByStatus = settlements
                .GroupBy(s => s.Status)
                .ToDictionary(g => g.Key, g => new StatusStat
                {
                    Count = g.Count(),
                    Total = g.Sum(s => s.Amount)
                }),
            ByCurrency = settlements
                .GroupBy(s => s.Currency)
                .ToDictionary(g => g.Key, g => new CurrencyStat
                {
                    Count = g.Count(),
                    Total = g.Sum(s => s.Amount)
                }),
            Settlements = settlements
        };
    }

    /// <summary>
    /// Returns daily incoming totals for a merchant within a date range.
    /// Useful for charting revenue over time.
    /// </summary>
    public async Task<List<DailyStat>> GetDailyIncomingAsync(
        Guid merchantId,
        Instant from,
        Instant to,
        string? currency = null)
    {
        var query = db.MerchantSettlements
            .Where(s => s.MerchantId == merchantId && s.CreatedAt >= from && s.CreatedAt <= to);

        if (!string.IsNullOrEmpty(currency))
            query = query.Where(s => s.Currency == currency);

        var settlements = await query.ToListAsync();

        return settlements
            .GroupBy(s => s.CreatedAt.InUtc().Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyStat
            {
                Date = g.Key,
                Count = g.Count(),
                Total = g.Sum(s => s.Amount),
                ByCurrency = g.GroupBy(s => s.Currency)
                    .ToDictionary(c => c.Key, c => c.Sum(s => s.Amount))
            })
            .ToList();
    }
}

public class MerchantOverviewStats
{
    public int TotalPending { get; set; }
    public int TotalSettled { get; set; }
    public int TotalAllTime { get; set; }
    public Dictionary<string, CurrencyStat> PendingByCurrency { get; set; } = new();
    public Dictionary<string, CurrencyStat> SettledByCurrency { get; set; } = new();
    public Dictionary<string, CurrencyStat> ThisMonthByCurrency { get; set; } = new();
}

public class PeriodIncomingResult
{
    public Instant From { get; set; }
    public Instant To { get; set; }
    public string? Currency { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public Dictionary<MerchantSettlementStatus, StatusStat> ByStatus { get; set; } = new();
    public Dictionary<string, CurrencyStat> ByCurrency { get; set; } = new();
    public List<SnMerchantSettlement> Settlements { get; set; } = new();
}

public class CurrencyStat
{
    public int Count { get; set; }
    public decimal Total { get; set; }
}

public class StatusStat
{
    public int Count { get; set; }
    public decimal Total { get; set; }
}

public class DailyStat
{
    public LocalDate Date { get; set; }
    public int Count { get; set; }
    public decimal Total { get; set; }
    public Dictionary<string, decimal> ByCurrency { get; set; } = new();
}
