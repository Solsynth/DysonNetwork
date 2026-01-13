using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Insight.Reader;

[DisallowConcurrentExecution]
public class WebFeedVerificationJob(
    WebFeedService webFeedService,
    ILogger<WebFeedVerificationJob> logger
)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting web feed verification job.");

        try
        {
            await webFeedService.VerifyAllFeedsAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during web feed verification job");
        }

        logger.LogInformation("Web feed verification job finished.");
    }
}
