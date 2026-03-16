using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Wallet.Payment;

public class LegacyInAppSubscriptionAvailabilityValidationService(
    IServiceScopeFactory scopeFactory,
    ILogger<LegacyInAppSubscriptionAvailabilityValidationService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var delay = await GetStartupDelayAsync(stoppingToken);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
            var affectedCount = await subscriptions.CancelUnavailableInAppWalletSubscriptionsAsync(stoppingToken);

            logger.LogInformation(
                "Completed legacy in-app subscription availability validation. Cancelled {AffectedCount} subscriptions.",
                affectedCount
            );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Legacy in-app subscription availability validation was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Legacy in-app subscription availability validation failed.");
        }
    }

    private async Task<TimeSpan> GetStartupDelayAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SubscriptionCatalogService>();
        var settings = catalog.GetSettings();
        var delaySeconds = Math.Max(0, settings.LegacyInAppAvailabilityValidationDelaySeconds);
        return TimeSpan.FromSeconds(delaySeconds);
    }
}
