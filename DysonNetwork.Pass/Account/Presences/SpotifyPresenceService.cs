using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account.Presences;

public class SpotifyPresenceService(
    AppDatabase db,
    Auth.OpenId.SpotifyOidcService spotifyService,
    AccountEventService accountEventService,
    ILogger<SpotifyPresenceService> logger
)
{
    /// <summary>
    /// Updates presence activities for users who have Spotify connections and are currently playing music
    /// </summary>
    public async Task UpdateAllSpotifyPresencesAsync()
    {
        var userConnections = await db.AccountConnections
            .Where(c => c.Provider == "spotify" && c.AccessToken != null && c.RefreshToken != null)
            .Include(c => c.Account)
            .ToListAsync();

        foreach (var connection in userConnections)
        {
            await UpdateSpotifyPresenceAsync(connection.Account);
        }
    }

    /// <summary>
    /// Updates the Spotify presence activity for a specific user
    /// </summary>
    private async Task UpdateSpotifyPresenceAsync(SnAccount account)
    {
        var connection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Provider == "spotify");

        if (connection?.RefreshToken == null)
        {
            // No Spotify connection, remove any existing Spotify presence
            await RemoveSpotifyPresenceAsync(account.Id);
            return;
        }

        try
        {
            var currentlyPlayingJson = await spotifyService.GetCurrentlyPlayingAsync(
                connection.RefreshToken,
                connection.AccessToken
            );

            if (string.IsNullOrEmpty(currentlyPlayingJson) || currentlyPlayingJson == "{}")
            {
                // Nothing playing, remove the presence
                await RemoveSpotifyPresenceAsync(account.Id);
                return;
            }

            var presenceActivity = await ParseAndCreatePresenceActivityAsync(account.Id, currentlyPlayingJson);

            // Update or create the presence activity
            await accountEventService.UpdateActivityByManualId(
                "spotify",
                account.Id,
                activity =>
                {
                    activity.Type = PresenceType.Music;
                    activity.Title = presenceActivity.Title;
                    activity.Subtitle = presenceActivity.Subtitle;
                    activity.Caption = presenceActivity.Caption;
                    activity.LargeImage = presenceActivity.LargeImage;
                    activity.SmallImage = presenceActivity.SmallImage;
                    activity.TitleUrl = presenceActivity.TitleUrl;
                    activity.SubtitleUrl = presenceActivity.SubtitleUrl;
                    activity.Meta = presenceActivity.Meta;
                },
                10 // 10 minute lease
            );
        }
        catch (Exception ex)
        {
            // On error, remove the presence to avoid stale data
            await RemoveSpotifyPresenceAsync(account.Id);

            // In a real implementation, you might want to log the error
            logger.LogError(ex, "Failed to update Spotify presence for user {UserId}", account.Id);
        }
    }

    /// <summary>
    /// Removes the Spotify presence activity for a user
    /// </summary>
    private async Task RemoveSpotifyPresenceAsync(Guid accountId)
    {
        await accountEventService.UpdateActivityByManualId(
            "spotify",
            accountId,
            activity =>
            {
                // Mark it for immediate expiration
                activity.LeaseExpiresAt = SystemClock.Instance.GetCurrentInstant();
            }
        );
    }

    private static Task<SnPresenceActivity> ParseAndCreatePresenceActivityAsync(Guid accountId, string currentlyPlayingJson)
    {
        var document = JsonDocument.Parse(currentlyPlayingJson);
        var root = document.RootElement;

        // Extract track information
        var item = root.GetProperty("item");
        var trackName = item.GetProperty("name").GetString() ?? "";
        var isPlaying = root.GetProperty("is_playing").GetBoolean();

        // Only create presence if actually playing
        if (!isPlaying)
        {
            throw new InvalidOperationException("Track is not currently playing");
        }

        // Get artists
        var artists = item.GetProperty("artists");
        var artistNames = artists.EnumerateArray()
            .Select(a => a.GetProperty("name").GetString() ?? "")
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
        var artistsString = string.Join(", ", artistNames);

        // Get album
        var album = item.GetProperty("album");
        var albumName = album.GetProperty("name").GetString() ?? "";

        // Get album images (artwork)
        string? albumImageUrl = null;
        if (album.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            var albumImages = images.EnumerateArray().ToList();
            // Take the largest image (usually last in the array)
            if (albumImages.Count > 0)
            {
                albumImageUrl = albumImages[0].GetProperty("url").GetString();
            }
        }

        // Get external URLs
        var externalUrls = item.GetProperty("external_urls");
        var trackUrl = externalUrls.GetProperty("spotify").GetString();

        var artistsUrls = new List<string>();
        foreach (var artist in artists.EnumerateArray())
        {
            if (artist.TryGetProperty("external_urls", out var artistUrls))
            {
                var spotifyUrl = artistUrls.GetProperty("spotify").GetString();
                if (!string.IsNullOrEmpty(spotifyUrl))
                {
                    artistsUrls.Add(spotifyUrl);
                }
            }
        }

        // Get progress and duration for metadata
        var progressMs = root.GetProperty("progress_ms").GetInt32();
        var durationMs = item.GetProperty("duration_ms").GetInt32();

        // Calculate progress percentage
        var progressPercent = durationMs > 0 ? (double)progressMs / durationMs * 100 : 0;

        // Get context info (playlist, album, etc.)
        string? contextType = null;
        string? contextUrl = null;
        if (root.TryGetProperty("context", out var context))
        {
            contextType = context.GetProperty("type").GetString();
            var contextExternalUrls = context.GetProperty("external_urls");
            contextUrl = contextExternalUrls.GetProperty("spotify").GetString();
        }

        return Task.FromResult(new SnPresenceActivity
        {
            AccountId = accountId,
            Type = PresenceType.Music,
            ManualId = "spotify",
            Title = trackName,
            Subtitle = artistsString,
            Caption = albumName,
            LargeImage = albumImageUrl,
            TitleUrl = trackUrl,
            SubtitleUrl = artistsUrls.FirstOrDefault(),
            Meta = new Dictionary<string, object>
            {
                ["track_duration_ms"] = durationMs,
                ["progress_ms"] = progressMs,
                ["progress_percent"] = progressPercent,
                ["track_id"] = item.GetProperty("id").GetString() ?? "",
                ["album_id"] = album.GetProperty("id").GetString() ?? "",
                ["artist_ids"] = artists.EnumerateArray().Select(a => a.GetProperty("id").GetString() ?? "").ToArray(),
                ["context_type"] = contextType,
                ["context_url"] = contextUrl,
                ["is_explicit"] = item.GetProperty("explicit").GetBoolean(),
                ["popularity"] = item.GetProperty("popularity").GetInt32(),
                ["spotify_track_url"] = trackUrl,
                ["updated_at"] = SystemClock.Instance.GetCurrentInstant()
            }
        });
    }
}
