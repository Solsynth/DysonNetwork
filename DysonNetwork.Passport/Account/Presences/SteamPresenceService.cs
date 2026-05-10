using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using NodaTime;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace DysonNetwork.Passport.Account.Presences;

public class SteamPresenceService(
    RemoteAccountConnectionService connections,
    AccountEventService accountEventService,
    ILogger<SteamPresenceService> logger,
    IConfiguration configuration
) : IPresenceService
{
    public sealed class SteamPresenceScanResult
    {
        public List<SteamPresenceScanItem> Items { get; set; } = [];
    }

    public sealed class SteamPresenceScanItem
    {
        public Guid AccountId { get; set; }
        public string SteamId { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? GameId { get; set; }
        public string? GameName { get; set; }
        public object? Raw { get; set; }
        public string? Error { get; set; }
    }

    /// <inheritdoc />
    public string ServiceId => "steam";

    /// <inheritdoc />
    public async Task UpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        await ScanAndUpdatePresencesAsync(userIds);
    }

    public async Task<SteamPresenceScanResult> ScanAndUpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        var steamConnections = new List<SnAccountConnection>();
        foreach (var userId in userIds.Distinct())
        {
            var userConnections = await connections.ListConnectionsAsync(userId, "steam");
            steamConnections.AddRange(userConnections.Where(c => !string.IsNullOrWhiteSpace(c.ProvidedIdentifier)));
        }

        if (steamConnections.Count == 0)
            return new SteamPresenceScanResult();

        var apiKey = configuration["Oidc:Steam:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Steam API key not configured, skipping presence update for {Count} users", steamConnections.Count);
            return new SteamPresenceScanResult
            {
                Items = steamConnections.Select(connection => new SteamPresenceScanItem
                {
                    AccountId = connection.AccountId,
                    SteamId = connection.ProvidedIdentifier,
                    Status = "missing_api_key",
                    Error = "Steam API key not configured"
                }).ToList()
            };
        }

        var result = new SteamPresenceScanResult();

        try
        {
            var webInterfaceFactory = new SteamWebInterfaceFactory(apiKey);
            var steamUserInterface = webInterfaceFactory.CreateSteamWebInterface<SteamUser>();

            var steamIds = steamConnections
                .Select(c => ulong.Parse(c.ProvidedIdentifier))
                .ToList();

            var playerSummariesResponse = await steamUserInterface.GetPlayerSummariesAsync(steamIds);
            var playerSummaries = playerSummariesResponse?.Data != null
                ? (IEnumerable<dynamic>)playerSummariesResponse.Data
                : [];

            var playerSummaryLookup = playerSummaries
                .ToDictionary(ps => ((dynamic)ps).SteamId.ToString(), ps => ps);

            foreach (var connection in steamConnections)
            {
                if (playerSummaryLookup.TryGetValue(connection.ProvidedIdentifier, out var playerSummaryData))
                {
                    result.Items.Add(await UpdateSteamPresenceFromDataAsync(connection.AccountId, connection.ProvidedIdentifier, playerSummaryData));
                }
                else
                {
                    await RemoveSteamPresenceAsync(connection.AccountId);
                    result.Items.Add(new SteamPresenceScanItem
                    {
                        AccountId = connection.AccountId,
                        SteamId = connection.ProvidedIdentifier,
                        Status = "not_found"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update Steam presence for {Count} users", steamConnections.Count);

            foreach (var connection in steamConnections)
            {
                try
                {
                    result.Items.Add(await UpdateSteamPresenceAsync(connection.AccountId, connection.ProvidedIdentifier));
                }
                catch (Exception individualEx)
                {
                    logger.LogError(individualEx, "Failed to update Steam presence for user {UserId}", connection.AccountId);
                    await RemoveSteamPresenceAsync(connection.AccountId);
                    result.Items.Add(new SteamPresenceScanItem
                    {
                        AccountId = connection.AccountId,
                        SteamId = connection.ProvidedIdentifier,
                        Status = "error",
                        Error = individualEx.Message
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Updates the Steam presence activity for a specific user using pre-fetched player summary data
    /// </summary>
    private async Task<SteamPresenceScanItem> UpdateSteamPresenceFromDataAsync(Guid accountId, string steamId, dynamic playerSummaryData)
    {
        if (!string.IsNullOrEmpty(playerSummaryData.PlayingGameId) && !string.IsNullOrEmpty(playerSummaryData.PlayingGameName))
        {
            var presenceActivity = ParsePlayerSummaryToPresenceActivity(accountId, playerSummaryData);

            var updatedActivity = await accountEventService.UpdateActivityByManualId(
                "steam",
                accountId,
                UpdateActivityWithPresenceData,
                10
            );

            if (updatedActivity == null)
                await accountEventService.SetActivity(presenceActivity, 10);

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

            return new SteamPresenceScanItem
            {
                AccountId = accountId,
                SteamId = steamId,
                Status = updatedActivity == null ? "created" : "updated",
                GameId = playerSummaryData.PlayingGameId?.ToString(),
                GameName = playerSummaryData.PlayingGameName,
                Raw = playerSummaryData
            };
        }

        await RemoveSteamPresenceAsync(accountId);
        return new SteamPresenceScanItem
        {
            AccountId = accountId,
            SteamId = steamId,
            Status = "removed",
            Raw = playerSummaryData
        };
    }

    /// <summary>
    /// Updates the Steam presence activity for a specific user (fallback individual API call)
    /// </summary>
    private async Task<SteamPresenceScanItem> UpdateSteamPresenceAsync(Guid accountId, string steamId)
    {
        try
        {
            var apiKey = configuration["Oidc:Steam:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogWarning("Steam API key not configured, skipping presence update for user {UserId}", accountId);
                return new SteamPresenceScanItem
                {
                    AccountId = accountId,
                    SteamId = steamId,
                    Status = "missing_api_key",
                    Error = "Steam API key not configured"
                };
            }

            var webInterfaceFactory = new SteamWebInterfaceFactory(apiKey);
            var steamUserInterface = webInterfaceFactory.CreateSteamWebInterface<SteamUser>();

            var playerSummaryResponse = await steamUserInterface.GetPlayerSummaryAsync(ulong.Parse(steamId));
            var playerSummaryData = playerSummaryResponse.Data;

            return await UpdateSteamPresenceFromDataAsync(accountId, steamId, playerSummaryData);
        }
        catch (Exception ex)
        {
            await RemoveSteamPresenceAsync(accountId);

            logger.LogError(ex, "Failed to update Steam presence for user {UserId}", accountId);
            return new SteamPresenceScanItem
            {
                AccountId = accountId,
                SteamId = steamId,
                Status = "error",
                Error = ex.Message
            };
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
