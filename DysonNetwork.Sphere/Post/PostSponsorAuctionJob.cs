using Quartz;

namespace DysonNetwork.Sphere.Post;

public class PostSponsorAuctionJob(SponsorService sponsorService, ILogger<PostSponsorAuctionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await sponsorService.RunHourlyAuctionAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run sponsor auction job");
        }
    }
}
