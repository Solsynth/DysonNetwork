using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SpotifyAPI.Web;

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
            await UpdateSpotifyPresenceAsync(connection.Account);
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
            // Ensure we have a valid access token
            var validToken = await spotifyService.GetValidAccessTokenAsync(connection.RefreshToken, connection.AccessToken);
            if (string.IsNullOrEmpty(validToken))
            {
                // Couldn't get a valid token, remove presence
                await RemoveSpotifyPresenceAsync(account.Id);
                return;
            }

            // Create Spotify client with the valid token
            var spotify = new SpotifyClient(validToken);

            // Get currently playing track
            var currentlyPlaying = await spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
            if (currentlyPlaying?.Item == null || !currentlyPlaying.IsPlaying)
            {
                // Nothing playing or paused, remove the presence
                await RemoveSpotifyPresenceAsync(account.Id);
                return;
            }

            var presenceActivity = ParseCurrentlyPlayingToPresenceActivity(account.Id, currentlyPlaying);

            // Try to update existing activity first
            var updatedActivity = await accountEventService.UpdateActivityByManualId(
                "spotify",
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
                activity.Type = PresenceType.Music;
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

    private static SnPresenceActivity ParseCurrentlyPlayingToPresenceActivity(Guid accountId, CurrentlyPlaying currentlyPlaying)
    {
        // Cast the item to FullTrack (it should be a track for music presence)
        if (currentlyPlaying.Item is not FullTrack track)
        {
            throw new InvalidOperationException("Currently playing item is not a track");
        }

        // Get track name
        var trackName = track.Name ?? "";
        if (string.IsNullOrEmpty(trackName))
        {
            throw new InvalidOperationException("Track name not available");
        }

        // Get artists
        var artists = track.Artists ?? new List<SimpleArtist>();
        var artistNames = artists.Select(a => a.Name).Where(name => !string.IsNullOrEmpty(name)).ToList();
        var artistsString = string.Join(", ", artistNames);

        // Get artist URLs
        var artistsUrls = artists
            .Where(a => a.ExternalUrls?.ContainsKey("spotify") == true)
            .Select(a => a.ExternalUrls!["spotify"])
            .ToList();

        // Get album info
        var album = track.Album;
        var albumName = album?.Name ?? "";
        string? albumImageUrl = null;

        // Get largest album image
        if (album?.Images != null && album.Images.Count > 0)
        {
            albumImageUrl = album.Images
                .OrderByDescending(img => img.Width)
                .FirstOrDefault()?.Url;
        }

        // Get track URL
        string? trackUrl = null;
        if (track.ExternalUrls?.ContainsKey("spotify") == true)
        {
            trackUrl = track.ExternalUrls["spotify"];
        }

        // Get progress and duration
        var progressMs = currentlyPlaying.ProgressMs ?? 0;
        var durationMs = track.DurationMs;
        var progressPercent = durationMs > 0 ? (double)progressMs / durationMs * 100 : 0;

        // Get context info
        var contextType = currentlyPlaying.Context?.Type;
        string? contextUrl = null;
        if (currentlyPlaying.Context?.ExternalUrls?.ContainsKey("spotify") == true)
        {
            contextUrl = currentlyPlaying.Context.ExternalUrls["spotify"];
        }

        // Build metadata
        var meta = new Dictionary<string, object>
        {
            ["track_duration_ms"] = durationMs,
            ["progress_ms"] = progressMs,
            ["progress_percent"] = progressPercent,
            ["spotify_track_url"] = trackUrl,
            ["updated_at"] = SystemClock.Instance.GetCurrentInstant()
        };

        // Add track ID
        if (!string.IsNullOrEmpty(track.Id))
            meta["track_id"] = track.Id;

        // Add album ID
        if (!string.IsNullOrEmpty(album?.Id))
            meta["album_id"] = album.Id;

        // Add artist IDs
        var artistIds = artists.Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
        meta["artist_ids"] = artistIds;

        // Add context info
        if (!string.IsNullOrEmpty(contextType))
            meta["context_type"] = contextType;
        if (!string.IsNullOrEmpty(contextUrl))
            meta["context_url"] = contextUrl;

        // Add track properties
        meta["is_explicit"] = track.Explicit;
        meta["popularity"] = track.Popularity;

        return new SnPresenceActivity
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
            Meta = meta
        };
    }
}
