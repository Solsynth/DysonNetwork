using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Insight.Thought;

public class ThoughtPartBackfillHostedService(
    IServiceProvider serviceProvider,
    ICacheService cache,
    ILogger<ThoughtPartBackfillHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var (acquired, insertedRows) = await cache.ExecuteWithLockAsync<int>(
                    "thinking-thought-part-backfill",
                    async () =>
                    {
                        using var scope = serviceProvider.CreateScope();
                        var thoughtService = scope.ServiceProvider.GetRequiredService<ThoughtService>();
                        return await thoughtService.BackfillThoughtPartRowsAsync(300, stoppingToken);
                    },
                    expiry: TimeSpan.FromMinutes(8),
                    waitTime: TimeSpan.FromSeconds(1),
                    retryInterval: TimeSpan.FromMilliseconds(200)
                );

                if (!acquired)
                {
                    continue;
                }

                if (insertedRows > 0)
                {
                    logger.LogInformation("Backfilled {Rows} thought part rows.", insertedRows);
                }
                else
                {
                    logger.LogDebug("No thought part rows needed backfill in this cycle.");
                }
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
}
