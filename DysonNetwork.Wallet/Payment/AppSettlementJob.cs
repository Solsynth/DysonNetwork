using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Wallet.Payment;

/// <summary>
/// Daily job that settles all pending merchant settlements.
/// Groups by wallet + currency to minimize transactions.
/// PostAward.SettledAt is handled separately by Sphere's SettlePostAwardsAsync.
/// </summary>
[DisallowConcurrentExecution]
public class AppSettlementJob(
    AppDatabase db,
    WalletService walletService,
    PaymentService paymentService,
    ILogger<AppSettlementJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("AppSettlementJob: starting daily settlement");

        var merchantService = new MerchantService(db);

        // Get all wallets with pending settlements
        var walletsWithPending = await db.MerchantSettlements
            .Where(s => s.Status == MerchantSettlementStatus.Pending)
            .Select(s => s.PaymentWalletId)
            .Distinct()
            .ToListAsync();

        logger.LogInformation(
            "AppSettlementJob: found {Count} wallets with pending settlements",
            walletsWithPending.Count);

        int totalSettled = 0;
        int totalErrors = 0;

        foreach (var walletId in walletsWithPending)
        {
            try
            {
                var transactions = await merchantService.SettleWalletAsync(
                    walletId,
                    MerchantSettlementTrigger.Automatic,
                    walletService,
                    paymentService);

                totalSettled += transactions.Count;
            }
            catch (Exception ex)
            {
                totalErrors++;
                logger.LogError(ex, "AppSettlementJob: failed to settle wallet {WalletId}", walletId);
            }
        }

        logger.LogInformation(
            "AppSettlementJob: done — {Settled} currency groups settled, {Errors} errors",
            totalSettled, totalErrors);
    }
}
