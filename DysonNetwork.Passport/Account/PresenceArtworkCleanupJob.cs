using Quartz;

namespace DysonNetwork.Passport.Account;

public class PresenceArtworkCleanupJob(
    PresenceArtworkService artworkService,
    ILogger<PresenceArtworkCleanupJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var removed = await artworkService.CleanupExpiredArtworksAsync(context.CancellationToken);
        if (removed > 0)
            logger.LogInformation("Presence artwork cleanup removed {Count} expired resources.", removed);
    }
}
