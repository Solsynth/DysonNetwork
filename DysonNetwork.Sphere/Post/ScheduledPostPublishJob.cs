using Quartz;

namespace DysonNetwork.Sphere.Post;

[DisallowConcurrentExecution]
public class ScheduledPostPublishJob(
    ScheduledPostPublishService scheduledPosts,
    ILogger<ScheduledPostPublishJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var count = await scheduledPosts.PublishDuePostsAsync(context.CancellationToken);
            if (count > 0)
                logger.LogInformation("Published {Count} scheduled posts", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish scheduled posts");
        }
    }
}
