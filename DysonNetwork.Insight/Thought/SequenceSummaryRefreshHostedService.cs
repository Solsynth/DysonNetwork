using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Insight.Thought;

public class SequenceSummaryRefreshHostedService(
    IServiceProvider serviceProvider,
    ICacheService cache,
    ILogger<SequenceSummaryRefreshHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        await RunOnceAsync(stoppingToken);

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var (acquired, refreshedCount) = await cache.ExecuteWithLockAsync<int>(
            "sequence-summary-refresh",
            async () =>
            {
                using var scope = serviceProvider.CreateScope();
                var thoughtService = scope.ServiceProvider.GetRequiredService<ThoughtService>();
                var count = await thoughtService.RefreshHistoricSequenceSummariesAsync(16, cancellationToken);
                return count;
            },
            expiry: TimeSpan.FromMinutes(10),
            waitTime: TimeSpan.FromSeconds(1),
            retryInterval: TimeSpan.FromMilliseconds(200)
        );

        if (!acquired)
        {
            logger.LogDebug("Skipped sequence summary refresh because another instance holds the lock.");
            return;
        }

        if (refreshedCount > 0)
        {
            logger.LogInformation("Refreshed summaries for {Count} sequences.", refreshedCount);
        }
    }
}
