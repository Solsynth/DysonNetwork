using System.Net.Http.Json;
using System.Text.Json;
using DysonNetwork.Pass;
using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Pass.Auth.OpenId;

public class DiscordOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "Discord";
    protected override string DiscoveryEndpoint => ""; // Discord doesn't have a standard OIDC discovery endpoint
    protected override string ConfigSectionName => "Discord";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "response_type", "code" },
            { "scope", "identify email" },
            { "state", state },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://discord.com/oauth2/authorize?{queryString}";
    }
    
    protected override Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        return Task.FromResult(new OidcDiscoveryDocument
        {
            AuthorizationEndpoint = "https://discord.com/oauth2/authorize",
            TokenEndpoint = "https://discord.com/oauth2/token",
            UserinfoEndpoint = "https://discord.com/users/@me",
            JwksUri = null
        })!;
    }
    
    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.AccessToken == null)
        {
            throw new InvalidOperationException("Failed to obtain access token from Discord");
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

        var response = await client.PostAsync("https://discord.com/oauth2/token", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string accessToken)
    {
        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/users/@me");
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