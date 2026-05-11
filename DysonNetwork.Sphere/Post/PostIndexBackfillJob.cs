using Quartz;

namespace DysonNetwork.Sphere.Post;

public class PostIndexBackfillJob(PostService postService, ILogger<PostIndexBackfillJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            logger.LogInformation("Starting historical post index backfill");
            await postService.BackfillPublicPostIndicesAsync(context.CancellationToken);
            logger.LogInformation("Historical post index backfill finished");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Historical post index backfill failed");
            throw;
        }
    }
}
