using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Wallet.Payment;

public class SubscriptionRenewalJob(
    AppDatabase db,
    SubscriptionService subscriptionService,
    PaymentService paymentService,
    WalletService walletService,
    ILogger<SubscriptionRenewalJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting subscription auto-renewal job...");

        // First update expired subscriptions
        var expiredCount = await subscriptionService.UpdateExpiredSubscriptionsAsync();
        logger.LogInformation("Updated {ExpiredCount} expired subscriptions", expiredCount);

        var now = SystemClock.Instance.GetCurrentInstant();
        const int batchSize = 100; // Process in smaller batches
        var processedCount = 0;
        var renewedCount = 0;
        var failedCount = 0;

        // Find subscriptions that need renewal (due for renewal and are still active)
        var subscriptionsToRenew = await db.WalletSubscriptions
            .Where(s => s.RenewalAt.HasValue && s.RenewalAt.Value <= now) // Due for renewal
            .Where(s => s.Status == SubscriptionStatus.Active) // Only paid subscriptions
            .Where(s => s.IsActive) // Only active subscriptions
            .Where(s => !s.IsFreeTrial) // Exclude free trials
            .OrderBy(s => s.RenewalAt) // Process oldest first
            .Take(batchSize)
            .Include(s => s.Coupon) // Include coupon information
            .ToListAsync();

        var totalSubscriptions = subscriptionsToRenew.Count;
        logger.LogInformation("Found {TotalSubscriptions} subscriptions due for renewal", totalSubscriptions);

        foreach (var subscription in subscriptionsToRenew)
        {
            try
            {
                processedCount++;
                logger.LogDebug(
                    "Processing renewal for subscription {SubscriptionId} (Identifier: {Identifier}) for account {AccountId}",
                    subscription.Id, subscription.Identifier, subscription.AccountId);

                if (subscription.RenewalAt is null)
                {
                    logger.LogWarning(
                        "Subscription {SubscriptionId} (Identifier: {Identifier}) has no renewal date or has been cancelled.",
                        subscription.Id, subscription.Identifier);
                    subscription.Status = SubscriptionStatus.Cancelled;
                    db.WalletSubscriptions.Update(subscription);
                    await db.SaveChangesAsync();
                    continue;
                }

                // Calculate next cycle duration based on current cycle
                var currentCycle = subscription.EndedAt!.Value - subscription.BegunAt;

                // Create an order for the renewal payment
                var order = await paymentService.CreateOrderAsync(
                    null,
                    WalletCurrency.GoldenPoint,
                    subscription.FinalPrice,
                    appIdentifier: "internal",
                    productIdentifier: subscription.Identifier,
                    meta: new Dictionary<string, object>()
                    {
                        ["subscription_id"] = subscription.Id.ToString(),
                        ["subscription_identifier"] = subscription.Identifier,
                        ["is_renewal"] = true
                    }
                );

                // Try to process the payment automatically
                if (subscription.PaymentMethod == SubscriptionPaymentMethod.InAppWallet)
                {
                    try
                    {
                        var wallet = await walletService.GetWalletAsync(subscription.AccountId);
                        if (wallet is null) continue;

                        // Process automatic payment from wallet
                        await paymentService.PayOrderAsync(order.Id, wallet);

                        // Update subscription details
                        subscription.BegunAt = subscription.EndedAt!.Value;
                        subscription.EndedAt = subscription.BegunAt.Plus(currentCycle);
                        subscription.RenewalAt = subscription.EndedAt;

                        db.WalletSubscriptions.Update(subscription);
                        await db.SaveChangesAsync();

                        renewedCount++;
                        logger.LogInformation("Successfully renewed subscription {SubscriptionId}", subscription.Id);
                    }
                    catch (Exception ex)
                    {
                        // If auto-payment fails, mark for manual payment
                        logger.LogWarning(ex, "Failed to auto-renew subscription {SubscriptionId} with wallet payment",
                            subscription.Id);
                        failedCount++;
                    }
                }
                else
                {
                    // For other payment methods, mark as pending payment
                    logger.LogInformation("Subscription {SubscriptionId} requires manual payment via {PaymentMethod}",
                        subscription.Id, subscription.PaymentMethod);
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing subscription {SubscriptionId}", subscription.Id);
                failedCount++;
            }

            // Log progress periodically
            if (processedCount % 20 == 0 || processedCount == totalSubscriptions)
            {
                logger.LogInformation(
                    "Progress: processed {ProcessedCount}/{TotalSubscriptions} subscriptions, {RenewedCount} renewed, {FailedCount} failed",
                    processedCount, totalSubscriptions, renewedCount, failedCount);
            }
        }

        logger.LogInformation(
            "Completed subscription renewal job. Processed: {ProcessedCount}, Renewed: {RenewedCount}, Failed: {FailedCount}",
            processedCount, renewedCount, failedCount);
    }
}
