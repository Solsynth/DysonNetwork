using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Padlock.Auth.OpenId;

public class SpotifyOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "spotify";
    protected override string DiscoveryEndpoint => "https://accounts.spotify.com/.well-known/openid-configuration";
    protected override string ConfigSectionName => "Spotify";

    public override async Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();

        if (discoveryDocument?.AuthorizationEndpoint == null)
            throw new InvalidOperationException("Authorization endpoint not found");

        var queryParams = BuildAuthorizationParameters(
            config.ClientId,
            config.RedirectUri,
            "openid email profile",
            "code",
            state,
            nonce
        );

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{discoveryDocument.AuthorizationEndpoint}?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.IdToken == null)
            throw new InvalidOperationException("Failed to get ID token");

        return new OidcUserInfo { Provider = ProviderName };
    }
    
    public override async Task<OidcTokenResponse?> RefreshTokenAsync(string refreshToken)
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

    public override async Task<string?> GetValidAccessTokenAsync(string refreshToken, string? currentAccessToken = null)
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
}
