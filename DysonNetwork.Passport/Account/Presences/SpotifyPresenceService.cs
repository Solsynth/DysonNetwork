using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using NodaTime;
using SpotifyAPI.Web;

namespace DysonNetwork.Passport.Account.Presences;

public class SpotifyPresenceService(
    RemoteAccountConnectionService connections,
    AccountEventService accountEventService,
    ILogger<SpotifyPresenceService> logger
) : IPresenceService
{
    /// <inheritdoc />
    public string ServiceId => "spotify";

    /// <inheritdoc />
    public async Task UpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds)
        {
            var userConnections = await connections.ListConnectionsAsync(userId, "spotify");
            foreach (var connection in userConnections.Where(c => c.RefreshToken != null))
                await UpdateSpotifyPresenceAsync(userId, connection);
        }
    }

    /// <summary>
    /// Updates the Spotify presence activity for a specific user
    /// </summary>
    private async Task UpdateSpotifyPresenceAsync(Guid accountId, SnAccountConnection connection)
    {
        if (connection.RefreshToken == null)
        {
            // No Spotify connection, remove any existing Spotify presence
            await RemoveSpotifyPresenceAsync(accountId);
            return;
        }

        try
        {
            var currentlyPlaying = await GetCurrentlyPlayingAsync(connection);
            if (currentlyPlaying?.Item == null || !currentlyPlaying.IsPlaying)
            {
                // Nothing playing, paused, or we couldn't get a usable token.
                await RemoveSpotifyPresenceAsync(accountId);
                return;
            }

            var presenceActivity = ParseCurrentlyPlayingToPresenceActivity(accountId, currentlyPlaying);

            // Try to update existing activity first
            var updatedActivity = await accountEventService.UpdateActivityByManualId(
                "spotify",
                accountId,
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
            await RemoveSpotifyPresenceAsync(accountId);

            // In a real implementation, you might want to log the error
            logger.LogError(ex, "Failed to update Spotify presence for user {UserId}", accountId);
        }
    }

    private async Task<CurrentlyPlaying?> GetCurrentlyPlayingAsync(SnAccountConnection connection)
    {
        var validToken = await connections.GetValidAccessTokenAsync(
            connection.Id,
            connection.RefreshToken!,
            connection.AccessToken);
        if (string.IsNullOrEmpty(validToken))
            return null;

        try
        {
            return await CreateSpotifyClient(validToken)
                .Player
                .GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
        }
        catch (APIUnauthorizedException)
        {
            // The token can expire after validation but before the player request completes.
            var refreshedToken = await connections.GetValidAccessTokenAsync(connection.Id, connection.RefreshToken!);
            if (string.IsNullOrEmpty(refreshedToken))
                return null;

            return await CreateSpotifyClient(refreshedToken)
                .Player
                .GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
        }
    }

    private static SpotifyClient CreateSpotifyClient(string accessToken) => new(accessToken);

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
