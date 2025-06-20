using System.Net.Http.Json;
using System.Text.Json;
using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Auth.OpenId;

public class AfdianOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "Afdian";
    protected override string DiscoveryEndpoint => ""; // Afdian doesn't have a standard OIDC discovery endpoint
    protected override string ConfigSectionName => "Afdian";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "response_type", "code" },
            { "scope", "basic" },
            { "state", state },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://afdian.com/oauth2/authorize?{queryString}";
    }
    
    protected override Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        return Task.FromResult(new OidcDiscoveryDocument
        {
            AuthorizationEndpoint = "https://afdian.com/oauth2/authorize",
            TokenEndpoint = "https://afdian.com/api/oauth2/access_token",
            UserinfoEndpoint = null,
            JwksUri = null
        })!;
    }
    
    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.AccessToken == null)
        {
            throw new InvalidOperationException("Failed to obtain access token from Afdian");
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

        var response = await client.PostAsync("https://afdian.com/api/oauth2/access_token", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string accessToken)
    {
        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var discordUser = JsonDocument.Parse(json).RootElement;

        var userId = discordUser.GetProperty("id").GetString() ?? "";
        var avatar = discordUser.TryGetProperty("avatar", out var avatarElement) ? avatarElement.GetString() : null;

        return new OidcUserInfo
        {
            UserId = userId,
            Email = (discordUser.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null) ?? "",
            EmailVerified = discordUser.TryGetProperty("verified", out var verifiedElement) &&
                            verifiedElement.GetBoolean(),
            DisplayName = (discordUser.TryGetProperty("global_name", out var globalNameElement)
                ? globalNameElement.GetString()
                : null) ?? "",
            PreferredUsername = discordUser.GetProperty("username").GetString() ?? "",
            ProfilePictureUrl = !string.IsNullOrEmpty(avatar)
                ? $"https://cdn.discordapp.com/avatars/{userId}/{avatar}.png"
                : "",
            Provider = ProviderName
        };
    }
}