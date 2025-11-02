using Quartz;

namespace DysonNetwork.Pass.Account.Presences;

public class SpotifyPresenceUpdateJob(SpotifyPresenceService spotifyPresenceService, ILogger<SpotifyPresenceUpdateJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting Spotify presence updates...");

        try
        {
            await spotifyPresenceService.UpdateAllSpotifyPresencesAsync();
            logger.LogInformation("Spotify presence updates completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during Spotify presence updates.");
        }
    }
}
