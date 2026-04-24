using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Insight.Thought;

public class ThoughtPartBackfillHostedService(
    IServiceProvider serviceProvider,
    ICacheService cache,
    ILogger<ThoughtPartBackfillHostedService> logger
) : BackgroundService
{
    private const int BackfillBatchSize = 300;
    private const int SummaryBatchSize = 16;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        await RunStartupCatchUpAsync(stoppingToken);

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
                        return await thoughtService.BackfillThoughtPartRowsAsync(BackfillBatchSize, stoppingToken);
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

    private async Task RunStartupCatchUpAsync(CancellationToken stoppingToken)
    {
        const int maxRounds = 200;
        var totalBackfilledRows = 0;
        var totalSummarizedSequences = 0;

        logger.LogInformation("Starting startup conversation-memory catch-up job.");

        for (var round = 1; round <= maxRounds; round++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var (acquired, stats) = await cache.ExecuteWithLockAsync<(int backfilledRows, int summarizedSequences)>(
                "thinking-startup-memory-catchup",
                async () =>
                {
                    using var scope = serviceProvider.CreateScope();
                    var thoughtService = scope.ServiceProvider.GetRequiredService<ThoughtService>();
                    var backfilledRows = await thoughtService.BackfillThoughtPartRowsAsync(BackfillBatchSize, stoppingToken);
                    var summarizedSequences = await thoughtService.RefreshHistoricSequenceSummariesAsync(SummaryBatchSize, stoppingToken);
                    return (backfilledRows, summarizedSequences);
                },
                expiry: TimeSpan.FromMinutes(10),
                waitTime: TimeSpan.FromSeconds(2),
                retryInterval: TimeSpan.FromMilliseconds(200)
            );

            if (!acquired)
            {
                logger.LogInformation("Skipped startup catch-up because another instance is handling it.");
                return;
            }

            totalBackfilledRows += stats.backfilledRows;
            totalSummarizedSequences += stats.summarizedSequences;

            if (stats.backfilledRows == 0 && stats.summarizedSequences == 0)
            {
                logger.LogInformation(
                    "Startup conversation-memory catch-up completed. rounds={Rounds}, backfilledRows={BackfilledRows}, summarizedSequences={SummarizedSequences}",
                    round,
                    totalBackfilledRows,
                    totalSummarizedSequences
                );
                return;
            }
        }

        logger.LogWarning(
            "Startup conversation-memory catch-up reached max rounds. backfilledRows={BackfilledRows}, summarizedSequences={SummarizedSequences}",
            totalBackfilledRows,
            totalSummarizedSequences
        );
    }
}
