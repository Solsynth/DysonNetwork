using System.Text.Json;
using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Pass.Auth.OpenId;

public class SteamOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "steam";
    protected override string DiscoveryEndpoint => ""; // Steam uses OpenID 2.0, not OIDC discovery
    protected override string ConfigSectionName => "Steam";

    public override Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        return Task.FromResult(GetAuthorizationUrl(state, nonce));
    }

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var returnUrl = config.RedirectUri;

        // Steam OpenID 2.0 authorization URL construction
        var queryParams = new Dictionary<string, string>
        {
            { "openid.ns", "http://specs.openid.net/auth/2.0" },
            { "openid.mode", "checkid_setup" },
            { "openid.return_to", returnUrl },
            { "openid.realm", new Uri(returnUrl).GetLeftPart(UriPartial.Authority) },
            { "openid.identity", "http://specs.openid.net/auth/2.0/identifier_select" },
            { "openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select" },
            // Store state in the return URL as a query parameter since Steam doesn't support state directly
            { "openid.state", state }
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://steamcommunity.com/openid/login?{queryString}";
    }

    protected override Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        // Steam doesn't use standard OIDC discovery
        return Task.FromResult<OidcDiscoveryDocument?>(null);
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        // Parse Steam's OpenID 2.0 response
        var queryParams = callbackData.QueryParameters;

        // Verify the OpenID response
        if (queryParams.GetValueOrDefault("openid.mode") != "id_res")
        {
            throw new InvalidOperationException("Invalid OpenID response mode");
        }

        // Extract Steam ID from claimed_id
        var claimedId = queryParams.GetValueOrDefault("openid.claimed_id");
        if (string.IsNullOrEmpty(claimedId))
        {
            throw new InvalidOperationException("No claimed_id in OpenID response");
        }

        // Steam ID is the last part of the claimed_id URL
        var steamId = claimedId.Split('/')[^1];
        if (!ulong.TryParse(steamId, out _))
        {
            throw new InvalidOperationException("Invalid Steam ID format");
        }

        // Fetch user information from Steam API
        var userInfo = await GetUserInfoAsync(steamId);

        return userInfo;
    }

    private async Task<OidcUserInfo> GetUserInfoAsync(string steamId)
    {
        var config = GetProviderConfig();
        var apiKey = Configuration[$"Oidc:Steam:ApiKey"] ?? throw new InvalidOperationException("Steam API key not configured");

        var client = HttpClientFactory.CreateClient();
        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={apiKey}&steamids={steamId}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var steamResponse = JsonDocument.Parse(json).RootElement;

        var players = steamResponse.GetProperty("response").GetProperty("players");
        if (players.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Steam user not found");
        }

        var player = players[0];
        var steamIdStr = player.GetProperty("steamid").GetString() ?? "";
        var personaName = player.GetProperty("personaname").GetString() ?? "";
        var avatarUrl = player.TryGetProperty("avatarfull", out var avatarElement)
            ? avatarElement.GetString()
            : player.TryGetProperty("avatarmedium", out var mediumAvatarElement)
                ? mediumAvatarElement.GetString()
                : null;

        return new OidcUserInfo
        {
            UserId = steamIdStr,
            DisplayName = personaName,
            PreferredUsername = personaName,
            ProfilePictureUrl = avatarUrl,
            Provider = ProviderName,
            // Steam OpenID doesn't provide email
            Email = null,
            EmailVerified = false
        };
    }
}
