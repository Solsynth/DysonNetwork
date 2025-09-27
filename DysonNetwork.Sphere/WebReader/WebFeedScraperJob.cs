using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Sphere.WebReader;

[DisallowConcurrentExecution]
public class WebFeedScraperJob(
    AppDatabase database,
    WebFeedService webFeedService,
    ILogger<WebFeedScraperJob> logger
)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting web feed scraper job.");

        var feeds = await database.Set<WebFeed>().ToListAsync(context.CancellationToken);

        foreach (var feed in feeds)
        {
            try
            {
                await webFeedService.ScrapeFeedAsync(feed, context.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape web feed {FeedId}", feed.Id);
            }
        }

        logger.LogInformation("Web feed scraper job finished.");
    }
}