using System.Text.Json;
using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Pass.Auth.OpenId;

public class SpotifyOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "Spotify";
    protected override string DiscoveryEndpoint => ""; // Spotify doesn't have a standard OIDC discovery endpoint
    protected override string ConfigSectionName => "Spotify";

    public override Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        return Task.FromResult(GetAuthorizationUrl(state, nonce));
    }

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "response_type", "code" },
            { "scope", "user-read-private user-read-email user-read-currently-playing" },
            { "state", state },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://accounts.spotify.com/authorize?{queryString}";
    }

    protected override Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        return Task.FromResult(new OidcDiscoveryDocument
        {
            AuthorizationEndpoint = "https://accounts.spotify.com/authorize",
            TokenEndpoint = "https://accounts.spotify.com/api/token",
            UserinfoEndpoint = "https://api.spotify.com/v1/me",
            JwksUri = null
        })!;
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.AccessToken == null)
        {
            throw new InvalidOperationException("Failed to obtain access token from Spotify");
        }

        var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);

        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;

        return userInfo;
    }

    protected override async Task<OidcTokenResponse?> ExchangeCodeForTokensAsync(string code,
        string? codeVerifier = null)
    {
        var config = GetProviderConfig();
        var client = HttpClientFactory.CreateClient();

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "client_secret", config.ClientSecret },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", config.RedirectUri },
        });

        var response = await client.PostAsync("https://accounts.spotify.com/api/token", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    /// <summary>
    /// Refreshes an access token using the refresh token
    /// </summary>
    public async Task<OidcTokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        var config = GetProviderConfig();
        var client = HttpClientFactory.CreateClient();

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "client_secret", config.ClientSecret },
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken },
        });

        var response = await client.PostAsync("https://accounts.spotify.com/api/token", content);
        if (!response.IsSuccessStatusCode)
        {
            return null; // Refresh failed
        }

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync(string refreshToken, string? currentAccessToken = null)
    {
        // If we don't have a current token, we need to refresh
        if (string.IsNullOrEmpty(currentAccessToken))
        {
            var refreshedTokens = await RefreshTokenAsync(refreshToken);
            return refreshedTokens?.AccessToken;
        }

        // Test if the current token is still valid by making a lightweight API call
        try
        {
            var client = HttpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
            request.Headers.Add("Authorization", $"Bearer {currentAccessToken}");

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                // Token is still valid
                return currentAccessToken;
            }

            // If unauthorized, try to refresh
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshedTokens = await RefreshTokenAsync(refreshToken);
                return refreshedTokens?.AccessToken;
            }
        }
        catch
        {
            // On any error, try to refresh
            var refreshedTokens = await RefreshTokenAsync(refreshToken);
            return refreshedTokens?.AccessToken;
        }

        // Current token is invalid and refresh failed
        return null;
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string accessToken)
    {
        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var spotifyUser = JsonDocument.Parse(json).RootElement;

        var userId = spotifyUser.GetProperty("id").GetString() ?? "";
        var email = spotifyUser.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;

        // Get display name - prefer display_name, then fallback to id
        var displayName = spotifyUser.TryGetProperty("display_name", out var displayNameElement)
            ? displayNameElement.GetString() ?? ""
            : userId;

        // Get profile picture - take the first image URL if available
        string? profilePictureUrl = null;
        if (spotifyUser.TryGetProperty("images", out var imagesElement) && imagesElement.ValueKind == JsonValueKind.Array)
        {
            var images = imagesElement.EnumerateArray().ToList();
            if (images.Count > 0)
            {
                profilePictureUrl = images[0].TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
            }
        }

        return new OidcUserInfo
        {
            UserId = userId,
            Email = email,
            DisplayName = displayName,
            PreferredUsername = userId, // Spotify doesn't have a separate username field like some platforms
            ProfilePictureUrl = profilePictureUrl,
            Provider = ProviderName
        };
    }

    /// <summary>
    /// Gets the user's currently playing track
    /// </summary>
    public async Task<string?> GetCurrentlyPlayingAsync(string refreshToken, string? currentAccessToken = null)
    {
        var validToken = await GetValidAccessTokenAsync(refreshToken, currentAccessToken);
        if (string.IsNullOrEmpty(validToken))
        {
            return null; // Couldn't get a valid token
        }

        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing");
        request.Headers.Add("Authorization", $"Bearer {validToken}");

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                // 204 No Content means nothing is currently playing
                return "{}";
            }

            // Try one more time with a fresh token if it failed with unauthorized
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var freshToken = await RefreshTokenAsync(refreshToken);
                if (freshToken?.AccessToken != null)
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", freshToken.AccessToken);
                    response = await client.SendAsync(request);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        return "{}";
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        return await response.Content.ReadAsStringAsync();
    }
}
