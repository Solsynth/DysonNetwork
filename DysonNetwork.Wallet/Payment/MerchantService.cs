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
}
