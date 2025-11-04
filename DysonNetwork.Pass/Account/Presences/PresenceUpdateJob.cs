using Quartz;

namespace DysonNetwork.Pass.Account.Presences;

public class PresenceUpdateJob(
    IEnumerable<IPresenceService> presenceServices,
    AccountEventService accountEventService,
    ILogger<PresenceUpdateJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // Get the stage parameter from the job data
        var stageString = context.MergedJobDataMap.GetString("stage");
        if (!Enum.TryParse<PresenceUpdateStage>(stageString, out var stage))
        {
            logger.LogError("Invalid or missing stage parameter: {Stage}", stageString);
            return;
        }

        logger.LogInformation("Starting presence updates for stage: {Stage}", stage);

        try
        {
            // Get users to update based on the stage
            var userIds = await GetUsersForStageAsync(stage);

            if (userIds.Count == 0)
            {
                logger.LogInformation("No users found for stage {Stage}", stage);
                return;
            }

            logger.LogInformation("Found {UserCount} users for stage {Stage}", userIds.Count, stage);

            // Update presence for each service
            foreach (var presenceService in presenceServices)
            {
                try
                {
                    await presenceService.UpdatePresencesAsync(userIds);
                    logger.LogInformation("Updated {ServiceId} presences for {UserCount} users in stage {Stage}",
                        presenceService.ServiceId, userIds.Count, stage);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error updating {ServiceId} presences for stage {Stage}",
                        presenceService.ServiceId, stage);
                }
            }

            logger.LogInformation("Presence updates completed for stage {Stage}", stage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during presence updates for stage {Stage}", stage);
        }
    }

    private async Task<List<Guid>> GetUsersForStageAsync(PresenceUpdateStage stage)
    {
        // Get all users with presence connections
        var allUserIds = await GetAllUsersWithPresenceConnectionsAsync();

        if (!allUserIds.Any())
        {
            return new List<Guid>();
        }

        // Batch fetch online status for all users
        var onlineStatuses = await accountEventService.GetAccountIsConnectedBatch(allUserIds);

        var filteredUserIds = new List<Guid>();

        foreach (var userId in allUserIds)
        {
            var userIdString = userId.ToString();
            var isOnline = onlineStatuses.GetValueOrDefault(userIdString, false);
            var activeActivities = await accountEventService.GetActiveActivities(userId);
            var hasActivePresence = activeActivities.Any();

            var shouldInclude = stage switch
            {
                PresenceUpdateStage.Active => isOnline && hasActivePresence,
                PresenceUpdateStage.Maybe => isOnline && !hasActivePresence,
                PresenceUpdateStage.Cold => !isOnline,
                _ => false
            };

            if (shouldInclude)
            {
                filteredUserIds.Add(userId);
            }
        }

        return filteredUserIds;
    }

    private async Task<List<Guid>> GetAllUsersWithPresenceConnectionsAsync()
    {
        // This method should return all users who have connections to any presence service
        // For now, we'll focus on Spotify users, but this should be extended to include all presence services

        // In a more complete implementation, you might want to query all presence services
        // to get users with connections to any of them

        // For simplicity, we'll return users with Spotify connections
        // This should be made more generic in the future
        var spotifyUsers = await accountEventService.GetSpotifyConnectedUsersAsync();
        return spotifyUsers;
    }
}
