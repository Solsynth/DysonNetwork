using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using NodaTime;

namespace DysonNetwork.Passport.Account.Presences;

public class LastFmPresenceService(
    RemoteAccountConnectionService connections,
    AccountEventService accountEventService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<LastFmPresenceService> logger
) : IPresenceService
{
    private const string ApiBase = "https://ws.audioscrobbler.com/2.0/";
    private const string RecentTracksMethod = "user.getRecentTracks";

    // Last.fm throttles an IP to five requests per second and the now-playing
    // feed is one request per user, which cannot be batched. The three presence
    // stages run as separate Quartz jobs against the same IP, so the spacing has
    // to be process-wide rather than per-run.
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(250);
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static long _nextRequestAt;

    private static async Task ThrottleAsync()
    {
        await RequestGate.WaitAsync();
        try
        {
            var wait = _nextRequestAt - Environment.TickCount64;
            if (wait > 0)
                await Task.Delay((int)wait);
            _nextRequestAt = Environment.TickCount64 + (long)RequestSpacing.TotalMilliseconds;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    public sealed class LastFmPresenceScanItem
    {
        public Guid AccountId { get; set; }
        public string Username { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? Track { get; set; }
        public string? Artist { get; set; }
        public string? Error { get; set; }
    }

    public sealed class LastFmPresenceScanResult
    {
        public List<LastFmPresenceScanItem> Items { get; set; } = [];
    }

    /// <inheritdoc />
    public string ServiceId => "lastfm";

    /// <inheritdoc />
    public async Task UpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        await ScanAndUpdatePresencesAsync(userIds);
    }

    public async Task<LastFmPresenceScanResult> ScanAndUpdatePresencesAsync(IEnumerable<Guid> userIds)
    {
        var result = new LastFmPresenceScanResult();

        var lastFmConnections = new List<SnAccountConnection>();
        foreach (var userId in userIds.Distinct())
        {
            var userConnections = await connections.ListConnectionsAsync(userId, ServiceId);
            lastFmConnections.AddRange(userConnections.Where(c => !string.IsNullOrWhiteSpace(c.ProvidedIdentifier)));
        }

        if (lastFmConnections.Count == 0)
            return result;

        var apiKey = configuration["Oidc:LastFm:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Last.fm API key not configured, skipping presence update for {Count} users",
                lastFmConnections.Count);
            result.Items.AddRange(lastFmConnections.Select(connection => new LastFmPresenceScanItem
            {
                AccountId = connection.AccountId,
                Username = connection.ProvidedIdentifier,
                Status = "missing_api_key",
                Error = "Last.fm API key not configured"
            }));
            return result;
        }

        var apiSecret = configuration["Oidc:LastFm:ApiSecret"];
        var http = httpClientFactory.CreateClient();

        foreach (var connection in lastFmConnections)
        {
            try
            {
                result.Items.Add(await ScanConnectionAsync(http, apiKey, apiSecret, connection));
            }
            catch (Exception ex)
            {
                // A transient Last.fm failure must not clear a live session; the
                // lease expires on its own if the feed stays unreachable.
                logger.LogError(ex, "Failed to update Last.fm presence for user {UserId}", connection.AccountId);
                result.Items.Add(new LastFmPresenceScanItem
                {
                    AccountId = connection.AccountId,
                    Username = connection.ProvidedIdentifier,
                    Status = "error",
                    Error = ex.Message
                });
            }
        }

        return result;
    }

    private async Task<LastFmPresenceScanItem> ScanConnectionAsync(
        HttpClient http,
        string apiKey,
        string? apiSecret,
        SnAccountConnection connection)
    {
        var username = connection.ProvidedIdentifier;
        var track = await GetNowPlayingAsync(http, apiKey, apiSecret, connection);

        if (track == null)
        {
            await accountEventService.EndActivitySession(connection.AccountId, ServiceId);
            return new LastFmPresenceScanItem
            {
                AccountId = connection.AccountId,
                Username = username,
                Status = "removed"
            };
        }

        await StartListeningSessionAsync(connection.AccountId, username, track);
        return new LastFmPresenceScanItem
        {
            AccountId = connection.AccountId,
            Username = username,
            Status = "updated",
            Track = track.Name,
            Artist = track.Artist
        };
    }

    private async Task<LastFmTrack?> GetNowPlayingAsync(
        HttpClient http,
        string apiKey,
        string? apiSecret,
        SnAccountConnection connection)
    {
        try
        {
            return await RequestNowPlayingAsync(http, apiKey, connection.ProvidedIdentifier, null);
        }
        catch (LastFmApiException ex) when (
            ex.Code == 17
            && !string.IsNullOrWhiteSpace(connection.AccessToken)
            && !string.IsNullOrWhiteSpace(apiSecret))
        {
            // A profile with "Hide recent listening information" refuses the
            // public feed (17: Login required). The web service session key
            // stored on the connection unlocks the user's own data through a
            // signed call.
            return await RequestNowPlayingAsync(
                http,
                apiKey,
                connection.ProvidedIdentifier,
                connection.AccessToken,
                apiSecret);
        }
    }

    private static async Task<LastFmTrack?> RequestNowPlayingAsync(
        HttpClient http,
        string apiKey,
        string username,
        string? sessionKey,
        string? apiSecret = null)
    {
        // Every request parameter except `format`/`callback` is part of the
        // signature, and the signature is computed before `api_sig` is added.
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["limit"] = "1",
            ["method"] = RecentTracksMethod,
            ["user"] = username
        };
        if (!string.IsNullOrWhiteSpace(sessionKey))
            parameters["sk"] = sessionKey;
        if (!string.IsNullOrWhiteSpace(sessionKey) && !string.IsNullOrWhiteSpace(apiSecret))
            parameters["api_sig"] = Sign(apiSecret, parameters);

        var query = string.Join(
            "&",
            parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
        );

        await ThrottleAsync();
        using var response = await http.GetAsync($"{ApiBase}?{query}&format=json");
        var raw = await response.Content.ReadAsStringAsync();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"lastfm {RecentTracksMethod} returned HTTP {(int)response.StatusCode}: {Truncate(raw)}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                var code = error.ValueKind == JsonValueKind.Number ? error.GetInt32() : 0;
                throw new LastFmApiException(code, GetText(root, "message"));
            }

            var current = GetFirstTrack(root);
            if (current == null || !IsNowPlaying(current.Value))
                return null;

            var element = current.Value;
            var name = GetText(element, "name");
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return new LastFmTrack(
                name,
                GetNestedText(element, "artist") ?? "Unknown Artist",
                GetNestedText(element, "album"),
                GetText(element, "mbid"),
                GetText(element, "url"),
                GetLargestImage(element));
        }
    }

    private async Task StartListeningSessionAsync(Guid accountId, string username, LastFmTrack track)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var catalogKey = !string.IsNullOrWhiteSpace(track.Mbid)
            ? track.Mbid
            : $"{track.Artist} - {track.Name}";

        var queryableTerms = new List<string>
        {
            ServiceId,
            track.Artist.ToLowerInvariant(),
            track.Name.ToLowerInvariant()
        };
        if (!string.IsNullOrWhiteSpace(track.Album))
            queryableTerms.Add(track.Album.ToLowerInvariant());

        var meta = new Dictionary<string, object?>
        {
            ["artist"] = track.Artist,
            ["album"] = track.Album,
            ["mbid"] = track.Mbid,
            ["url"] = track.Url,
            ["lastfm_profile_url"] = $"https://www.last.fm/user/{username}",
            ["updated_at"] = now
        };

        await accountEventService.StartActivitySession(
            accountId,
            ServiceId,
            "listening",
            new List<SnPresenceTag> { new() { Slug = "music", Name = "Music" } },
            ServiceId,
            catalogKey,
            track.Name,
            track.Artist,
            track.Album,
            track.ImageUrl,
            null,
            track.Url,
            $"https://www.last.fm/music/{Uri.EscapeDataString(track.Artist)}",
            queryableTerms.ToArray(),
            meta,
            catalogKey: catalogKey,
            catalogName: track.Name,
            visibility: PresenceVisibility.Public,
            leaseMinutes: 10
        );
    }

    private static JsonElement? GetFirstTrack(JsonElement root)
    {
        if (!root.TryGetProperty("recenttracks", out var recentTracks)
            || recentTracks.ValueKind != JsonValueKind.Object
            || !recentTracks.TryGetProperty("track", out var tracks))
            return null;

        return tracks.ValueKind switch
        {
            // `limit=1` returns the now-playing track only, but the feed
            // collapses to a bare object when a single scrobble is returned.
            JsonValueKind.Array => tracks.GetArrayLength() > 0 ? tracks[0] : null,
            JsonValueKind.Object => tracks,
            _ => null
        };
    }

    private static bool IsNowPlaying(JsonElement track)
    {
        if (!track.TryGetProperty("@attr", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            return false;
        if (!attributes.TryGetProperty("nowplaying", out var nowPlaying))
            return false;

        return nowPlaying.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(nowPlaying.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string? GetText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string? GetNestedText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return GetText(value, "#text") ?? GetText(value, "name");
    }

    private static string? GetLargestImage(JsonElement track)
    {
        if (!track.TryGetProperty("image", out var images) || images.ValueKind != JsonValueKind.Array)
            return null;

        string? largest = null;
        foreach (var image in images.EnumerateArray())
        {
            var text = GetText(image, "#text");
            if (!string.IsNullOrWhiteSpace(text))
                largest = text;
        }

        return largest;
    }

    private static string Sign(string secret, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        // Last.fm signature: alphabetically ordered <name><value> pairs with the
        // shared secret appended, md5 hashed (lowercase hex).
        var builder = new StringBuilder();
        foreach (var pair in parameters)
            builder.Append(pair.Key).Append(pair.Value);
        builder.Append(secret);

        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Truncate(string value) => value.Length <= 512 ? value : value[..512];

    private sealed record LastFmTrack(
        string Name,
        string Artist,
        string? Album,
        string? Mbid,
        string? Url,
        string? ImageUrl);

    private sealed class LastFmApiException(int code, string? message)
        : Exception($"lastfm {RecentTracksMethod} error {code}: {message}")
    {
        public int Code { get; } = code;
    }
}
