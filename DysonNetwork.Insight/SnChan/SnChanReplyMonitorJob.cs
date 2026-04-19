using DysonNetwork.Insight.Thought;
using Quartz;

namespace DysonNetwork.Insight.SnChan;

[DisallowConcurrentExecution]
public class SnChanReplyMonitorJob(
    SnChanReplyMonitorService monitorService,
    ILogger<SnChanReplyMonitorJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogDebug("SnChan reply monitor job starting...");

        try
        {
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