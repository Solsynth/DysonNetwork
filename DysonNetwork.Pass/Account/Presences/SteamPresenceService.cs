using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace DysonNetwork.Pass.Account.Presences;

public class SteamPresenceService(
    AppDatabase db,
    AccountEventService accountEventService,
    ILogger<SteamPresenceService> logger,
    IConfiguration configuration
) : IPresenceService
{
    /// <inheritdoc />
    public string ServiceId => "steam";

    /// <inheritdoc />
    public async Task UpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        var userIdList = userIds.ToList();
        var steamConnections = await db.AccountConnections
            .Where(c => userIdList.Contains(c.AccountId) && c.Provider == "steam")
            .Include(c => c.Account)
            .ToListAsync();

        if (steamConnections.Count == 0)
            return;

        // Get Steam API key from configuration
        var apiKey = configuration["Oidc:Steam:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Steam API key not configured, skipping presence update for {Count} users", steamConnections.Count);
            return;
        }

        try
        {
            // Create Steam Web API client
            var webInterfaceFactory = new SteamWebInterfaceFactory(apiKey);
            var steamUserInterface = webInterfaceFactory.CreateSteamWebInterface<SteamUser>();

            // Collect all Steam IDs for batch request
            var steamIds = steamConnections
                .Select(c => ulong.Parse(c.ProvidedIdentifier))
                .ToList();

            // Make batch API call (Steam supports up to 100 IDs per request)
            var playerSummariesResponse = await steamUserInterface.GetPlayerSummariesAsync(steamIds);
            var playerSummaries = playerSummariesResponse?.Data != null
                ? (IEnumerable<dynamic>)playerSummariesResponse.Data
                : new List<dynamic>();

            // Create a lookup dictionary for quick access
            var playerSummaryLookup = playerSummaries
                .ToDictionary(ps => ((dynamic)ps).SteamId.ToString(), ps => ps);

            // Process each connection
            foreach (var connection in steamConnections)
            {
                if (playerSummaryLookup.TryGetValue(connection.ProvidedIdentifier, out var playerSummaryData))
                {
                    await UpdateSteamPresenceFromDataAsync(connection.Account, playerSummaryData);
                }
                else
                {
                    // No data for this user, remove any existing presence
                    await RemoveSteamPresenceAsync(connection.Account.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update Steam presence for {Count} users", steamConnections.Count);

            // On batch error, fall back to individual calls to avoid losing all presence data
            foreach (var connection in steamConnections)
            {
                try
                {
                    await UpdateSteamPresenceAsync(connection.Account, connection.ProvidedIdentifier);
                }
                catch (Exception individualEx)
                {
                    logger.LogError(individualEx, "Failed to update Steam presence for user {UserId}", connection.Account.Id);
                    await RemoveSteamPresenceAsync(connection.Account.Id);
                }
            }
        }
    }

    /// <summary>
    /// Updates the Steam presence activity for a specific user using pre-fetched player summary data
    /// </summary>
    private async Task UpdateSteamPresenceFromDataAsync(SnAccount account, dynamic playerSummaryData)
    {
        if (!string.IsNullOrEmpty(playerSummaryData.PlayingGameId) && !string.IsNullOrEmpty(playerSummaryData.PlayingGameName))
        {
            // User is playing a game
            var presenceActivity = ParsePlayerSummaryToPresenceActivity(account.Id, playerSummaryData);

            // Try to update existing activity first
            var updatedActivity = await accountEventService.UpdateActivityByManualId(
                "steam",
                account.Id,
                UpdateActivityWithPresenceData,
                5
            );

            // If update failed (no existing activity), create a new one
            if (updatedActivity == null)
                await accountEventService.SetActivity(presenceActivity, 5);

            // Local function to avoid capturing external variables in lambda
            void UpdateActivityWithPresenceData(SnPresenceActivity activity)
            {
                activity.Type = PresenceType.Gaming;
                activity.Title = presenceActivity.Title;
                activity.Subtitle = presenceActivity.Subtitle;
                activity.Caption = presenceActivity.Caption;
                activity.LargeImage = presenceActivity.LargeImage;
                activity.SmallImage = presenceActivity.SmallImage;
                activity.TitleUrl = presenceActivity.TitleUrl;
                activity.SubtitleUrl = presenceActivity.SubtitleUrl;
                activity.Meta = presenceActivity.Meta;
            }
        }
        else
        {
            // User is not playing a game, remove any existing Steam presence
            await RemoveSteamPresenceAsync(account.Id);
        }
    }

    /// <summary>
    /// Updates the Steam presence activity for a specific user (fallback individual API call)
    /// </summary>
    private async Task UpdateSteamPresenceAsync(SnAccount account, string steamId)
    {
        try
        {
            // Get Steam API key from configuration
            var apiKey = configuration["Oidc:Steam:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("Steam API key not configured, skipping presence update for user {UserId}", account.Id);
                return;
            }

            // Create Steam Web API client
            var webInterfaceFactory = new SteamWebInterfaceFactory(apiKey);
            var steamUserInterface = webInterfaceFactory.CreateSteamWebInterface<SteamUser>();

            // Get player summary
            var playerSummaryResponse = await steamUserInterface.GetPlayerSummaryAsync(ulong.Parse(steamId));
            var playerSummaryData = playerSummaryResponse.Data;

            if (!string.IsNullOrEmpty(playerSummaryData.PlayingGameId) && !string.IsNullOrEmpty(playerSummaryData.PlayingGameName))
            {
                // User is playing a game
                var presenceActivity = ParsePlayerSummaryToPresenceActivity(account.Id, playerSummaryData);

                // Try to update existing activity first
                var updatedActivity = await accountEventService.UpdateActivityByManualId(
                    "steam",
                    account.Id,
                    UpdateActivityWithPresenceData,
                    5
                );

                // If update failed (no existing activity), create a new one
                if (updatedActivity == null)
                    await accountEventService.SetActivity(presenceActivity, 5);

                // Local function to avoid capturing external variables in lambda
                void UpdateActivityWithPresenceData(SnPresenceActivity activity)
                {
                    activity.Type = PresenceType.Gaming;
                    activity.Title = presenceActivity.Title;
                    activity.Subtitle = presenceActivity.Subtitle;
                    activity.Caption = presenceActivity.Caption;
                    activity.LargeImage = presenceActivity.LargeImage;
                    activity.SmallImage = presenceActivity.SmallImage;
                    activity.TitleUrl = presenceActivity.TitleUrl;
                    activity.SubtitleUrl = presenceActivity.SubtitleUrl;
                    activity.Meta = presenceActivity.Meta;
                }
            }
            else
            {
                // User is not playing a game, remove any existing Steam presence
                await RemoveSteamPresenceAsync(account.Id);
            }
        }
        catch (Exception ex)
        {
            // On error, remove the presence to avoid stale data
            await RemoveSteamPresenceAsync(account.Id);

            logger.LogError(ex, "Failed to update Steam presence for user {UserId}", account.Id);
        }
    }

    /// <summary>
    /// Removes the Steam presence activity for a user
    /// </summary>
    private async Task RemoveSteamPresenceAsync(Guid accountId)
    {
        await accountEventService.UpdateActivityByManualId(
            "steam",
            accountId,
            activity =>
            {
                // Mark it for immediate expiration
                activity.LeaseExpiresAt = SystemClock.Instance.GetCurrentInstant();
            }
        );
    }

    private static SnPresenceActivity ParsePlayerSummaryToPresenceActivity(Guid accountId, dynamic playerSummary)
    {
        var gameName = playerSummary.PlayingGameName ?? "Unknown Game";
        var gameId = playerSummary.PlayingGameId?.ToString() ?? "";

        // Build metadata
        var meta = new Dictionary<string, object>
        {
            ["game_id"] = gameId,
            ["steam_profile_url"] = $"https://steamcommunity.com/profiles/{playerSummary.SteamId}",
            ["updated_at"] = SystemClock.Instance.GetCurrentInstant()
        };

        return new SnPresenceActivity
        {
            AccountId = accountId,
            Type = PresenceType.Gaming,
            ManualId = "steam",
            Title = gameName,
            Subtitle = "Playing on Steam",
            Caption = null, // Could be game details if available
            LargeImage = null, // Could fetch game icon from Steam API if needed
            SmallImage = null,
            TitleUrl = $"https://store.steampowered.com/app/{gameId}",
            SubtitleUrl = $"https://steamcommunity.com/profiles/{playerSummary.SteamId}",
            Meta = meta
        };
    }
}
