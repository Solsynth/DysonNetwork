using DysonNetwork.Insight.Thought;
using Quartz;

namespace DysonNetwork.Insight.SnChan;

[DisallowConcurrentExecution]
public class SnChanReplyMonitorJob(
    SnChanReplyMonitorService monitorService,
    SnChanPublisherService publisherService,
    ILogger<SnChanReplyMonitorJob> logger
) : IJob
{
    private static bool _publisherInitialized = false;

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogDebug("SnChan reply monitor job starting...");

        try
        {
            // Initialize publisher service on first run
            if (!_publisherInitialized)
            {
                await publisherService.InitializeAsync(context.CancellationToken);
                _publisherInitialized = true;
            }

            await monitorService.CheckAndRespondToMentionsAsync(context.CancellationToken);
            await monitorService.CheckAndRespondToRepliesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SnChan reply monitor job");
        }

        logger.LogDebug("SnChan reply monitor job completed.");
    }
}