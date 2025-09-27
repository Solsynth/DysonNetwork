using System.Text.Json;
using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Pass.Auth.OpenId;

public class GitHubOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "GitHub";
    protected override string DiscoveryEndpoint => ""; // GitHub doesn't have a standard OIDC discovery endpoint
    protected override string ConfigSectionName => "GitHub";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "scope", "user:email" },
            { "state", state },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://github.com/login/oauth/authorize?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.AccessToken == null)
        {
            throw new InvalidOperationException("Failed to obtain access token from GitHub");
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

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", config.ClientId },
                { "client_secret", config.ClientSecret },
                { "code", code },
                { "redirect_uri", config.RedirectUri },
            })
        };
        tokenRequest.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(tokenRequest);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string accessToken)
    {
        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("User-Agent", "DysonNetwork.Pass");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var githubUser = JsonDocument.Parse(json).RootElement;

        var email = githubUser.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
        if (string.IsNullOrEmpty(email))
        {
            email = await GetPrimaryEmailAsync(accessToken);
        }

        return new OidcUserInfo
        {
            UserId = githubUser.GetProperty("id").GetInt64().ToString(),
            Email = email,
            DisplayName = githubUser.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "",
            PreferredUsername = githubUser.GetProperty("login").GetString() ?? "",
            ProfilePictureUrl = githubUser.TryGetProperty("avatar_url", out var avatarElement)
                ? avatarElement.GetString() ?? ""
                : "",
            Provider = ProviderName
        };
    }

    private async Task<string?> GetPrimaryEmailAsync(string accessToken)
    {
        var client = HttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("User-Agent", "DysonNetwork.Pass");

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var emails = await response.Content.ReadFromJsonAsync<List<GitHubEmail>>();
        return emails?.FirstOrDefault(e => e.Primary)?.Email;
    }

    private class GitHubEmail
    {
        public string Email { get; set; } = "";
        public bool Primary { get; set; }
        public bool Verified { get; set; }
    }
}