using System.Net.Http.Json;
using System.Text.Json;

namespace DysonNetwork.Sphere.Auth.OpenId;

public class MicrosoftOidcService : OidcService
{
    public MicrosoftOidcService(IConfiguration configuration, IHttpClientFactory httpClientFactory, AppDatabase db)
        : base(configuration, httpClientFactory, db)
    {
    }

    public override string ProviderName => "Microsoft";

    protected override string DiscoveryEndpoint => _configuration[$"Oidc:{ConfigSectionName}:DiscoveryEndpoint"] ?? throw new InvalidOperationException("Microsoft OIDC discovery endpoint is not configured.");

    protected override string ConfigSectionName => "Microsoft";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var discoveryDocument = GetDiscoveryDocumentAsync().GetAwaiter().GetResult();
        if (discoveryDocument?.AuthorizationEndpoint == null)
            throw new InvalidOperationException("Authorization endpoint not found in discovery document.");

        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "response_type", "code" },
            { "redirect_uri", config.RedirectUri },
            { "response_mode", "query" },
            { "scope", "openid profile email" },
            { "state", state },
            { "nonce", nonce },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{discoveryDocument.AuthorizationEndpoint}?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code);
        if (tokenResponse?.AccessToken == null)
        {
            throw new InvalidOperationException("Failed to obtain access token from Microsoft");
        }

        var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);

        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;

        return userInfo;
    }

    protected override async Task<OidcTokenResponse?> ExchangeCodeForTokensAsync(string code, string? codeVerifier = null)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();
        if (discoveryDocument?.TokenEndpoint == null)
        {
            throw new InvalidOperationException("Token endpoint not found in discovery document.");
        }

        var client = _httpClientFactory.CreateClient();

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, discoveryDocument.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", config.ClientId },
                { "scope", "openid profile email" },
                { "code", code },
                { "redirect_uri", config.RedirectUri },
                { "grant_type", "authorization_code" },
                { "client_secret", config.ClientSecret },
            })
        };

        var response = await client.SendAsync(tokenRequest);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string accessToken)
    {
        var discoveryDocument = await GetDiscoveryDocumentAsync();
        if (discoveryDocument?.UserinfoEndpoint == null)
            throw new InvalidOperationException("Userinfo endpoint not found in discovery document.");

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, discoveryDocument.UserinfoEndpoint);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var microsoftUser = JsonDocument.Parse(json).RootElement;

        return new OidcUserInfo
        {
            UserId = microsoftUser.GetProperty("sub").GetString() ?? "",
            Email = microsoftUser.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null,
            DisplayName = microsoftUser.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "",
            PreferredUsername = microsoftUser.TryGetProperty("preferred_username", out var preferredUsernameElement) ? preferredUsernameElement.GetString() ?? "" : "",
            ProfilePictureUrl = microsoftUser.TryGetProperty("picture", out var pictureElement) ? pictureElement.GetString() ?? "" : "",
            Provider = ProviderName
        };
    }
}
