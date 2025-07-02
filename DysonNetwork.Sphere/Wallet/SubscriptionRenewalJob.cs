using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Wallet;

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
                    appIdentifier: SubscriptionService.SubscriptionOrderIdentifier,
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
                        await paymentService.PayOrderAsync(order.Id, wallet.Id);

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

        logger.LogInformation("Validating user stellar memberships...");

        // Get all account IDs with StellarMembership
        var accountsWithMemberships = await db.AccountProfiles
            .Where(a => a.StellarMembership != null)
            .Select(a => new { a.Id, a.StellarMembership })
            .ToListAsync();

        logger.LogInformation("Found {Count} accounts with stellar memberships to validate",
            accountsWithMemberships.Count);

        if (accountsWithMemberships.Count == 0)
        {
            logger.LogInformation("No stellar memberships found to validate");
            return;
        }

        // Get all subscription IDs from StellarMemberships
        var memberships = accountsWithMemberships
            .Where(a => a.StellarMembership != null)
            .Select(a => a.StellarMembership)
            .Distinct()
            .ToList();
        var membershipIds = memberships.Select(m => m!.Id).ToList();

        // Get all valid subscriptions in a single query
        var validSubscriptions = await db.WalletSubscriptions
            .Where(s => membershipIds.Contains(s.Id))
            .Where(s => s.IsActive)
            .ToListAsync();
        var validSubscriptionsId = validSubscriptions
            .Where(s => s.IsAvailable)
            .Select(s => s.Id)
            .ToList();

        // Identify accounts that need updating (membership expired or not in validSubscriptions)
        var accountIdsToUpdate = accountsWithMemberships
            .Where(a => a.StellarMembership != null && !validSubscriptionsId.Contains(a.StellarMembership.Id))
            .Select(a => a.Id)
            .ToList();

        // Log the IDs that will be updated for debugging
        logger.LogDebug("Accounts with expired or invalid memberships: {AccountIds}",
            string.Join(", ", accountIdsToUpdate));

        if (accountIdsToUpdate.Count == 0)
        {
            logger.LogInformation("No expired/invalid stellar memberships found");
            return;
        }

        // Update all accounts in a single batch operation
        var updatedCount = await db.AccountProfiles
            .Where(a => accountIdsToUpdate.Contains(a.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.StellarMembership, p => null)
            );

        logger.LogInformation("Updated {Count} accounts with expired/invalid stellar memberships", updatedCount);
    }
}