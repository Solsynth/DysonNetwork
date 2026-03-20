using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Padlock.Account;

namespace DysonNetwork.Padlock.Auth.OpenId;

public class AfdianOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache,
    ILogger<AfdianOidcService> logger,
    ActionLogService actionLogs
)
    : OidcService(configuration, httpClientFactory, db, auth, cache, actionLogs)
{
    public override string ProviderName => "Afdian";
    protected override string DiscoveryEndpoint => ""; // Afdian doesn't have a standard OIDC discovery endpoint
    protected override string ConfigSectionName => "Afdian";

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
            { "scope", "basic" },
            { "state", state },
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://ifdian.net/oauth2/authorize?{queryString}";
    }

    protected override Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        return Task.FromResult(new OidcDiscoveryDocument
        {
            AuthorizationEndpoint = "https://ifdian.net/oauth2/authorize",
            TokenEndpoint = "https://ifdian.net/api/oauth2/access_token",
            UserinfoEndpoint = null,
            JwksUri = null
        })!;
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        try
        {
            var config = GetProviderConfig();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", config.ClientId },
                { "client_secret", config.ClientSecret },
                { "grant_type", "authorization_code" },
                { "code", callbackData.Code },
                { "redirect_uri", config.RedirectUri },
            });

            var client = HttpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://ifdian.net/api/oauth2/access_token");
            request.Content = content;
            
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Trying get userinfo from afdian, response: {Response}", json);
            var afdianResponse = JsonDocument.Parse(json).RootElement;

            var user = afdianResponse.TryGetProperty("data", out var dataElement) ? dataElement : default;
            var userId = user.TryGetProperty("user_id", out var userIdElement) ? userIdElement.GetString() ?? "" : "";
            var avatar = user.TryGetProperty("avatar", out var avatarElement) ? avatarElement.GetString() : null;

            return new OidcUserInfo
            {
                UserId = userId,
                DisplayName = (user.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null) ?? "",
                ProfilePictureUrl = avatar,
                Provider = ProviderName
            };
        }
        catch (Exception ex)
        {
            // Due to afidan's API isn't compliant with OAuth2, we want more logs from it to investigate.
            logger.LogError(ex, "Failed to get user info from Afdian");
            throw;
        }
    }
}
