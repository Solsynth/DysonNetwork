using DysonNetwork.Insight.Thought;
using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Insight.MiChan;

public class MiChanSequenceUnificationHostedService(
    IServiceProvider serviceProvider,
    ICacheService cache,
    ILogger<MiChanSequenceUnificationHostedService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (acquired, _) = await cache.ExecuteWithLockAsync<int>(
            "michan-sequence-unification",
            async () =>
            {
                using var scope = serviceProvider.CreateScope();
                var thoughtService = scope.ServiceProvider.GetRequiredService<ThoughtService>();
                var mergedCount = await thoughtService.MergeHistoricMiChanSequencesAsync(cancellationToken);
                logger.LogInformation("MiChan sequence unification startup pass completed. Merged {Count} sequences.", mergedCount);
                return mergedCount;
            },
            expiry: TimeSpan.FromMinutes(10),
            waitTime: TimeSpan.FromSeconds(1),
            retryInterval: TimeSpan.FromMilliseconds(200)
        );

        if (!acquired)
        {
            logger.LogInformation("Skipped MiChan sequence unification startup pass because another instance holds the lock.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
