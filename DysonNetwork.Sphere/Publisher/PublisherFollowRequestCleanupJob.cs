using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherFollowRequestCleanupJob(
    AppDatabase db,
    PublisherService publisherService,
    ILogger<PublisherFollowRequestCleanupJob> logger,
    IClock clock)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting publisher follow request cleanup job");

        try
        {
            var deletedCount = await publisherService.CleanupExpiredFollowRequests();
            logger.LogInformation("Publisher follow request cleanup completed. Deleted {Count} expired requests", deletedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing publisher follow request cleanup job");
        }
    }
}
