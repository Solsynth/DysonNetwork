using DysonNetwork.Shared.Cache;
using System.Web;

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

        // Steam OpenID 2.0 doesn't support state parameter directly
        // Include state in the return URL as a query parameter
        var returnUrlWithState = $"{returnUrl}{(returnUrl.Contains('?') ? '&' : '?')}state={Uri.EscapeDataString(state)}";

        // Steam OpenID 2.0 authorization URL construction
        var queryParams = new Dictionary<string, string>
        {
            { "openid.ns", "http://specs.openid.net/auth/2.0" },
            { "openid.mode", "checkid_setup" },
            { "openid.return_to", returnUrlWithState },
            { "openid.realm", new Uri(returnUrl).GetLeftPart(UriPartial.Authority) },
            { "openid.identity", "http://specs.openid.net/auth/2.0/identifier_select" },
            { "openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select" }
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

        // Extract state from the return_to URL since Steam doesn't support state parameter directly
        var returnTo = queryParams.GetValueOrDefault("openid.return_to");
        if (!string.IsNullOrEmpty(returnTo))
        {
            var returnToUri = new Uri(returnTo);
            var stateParam = System.Web.HttpUtility.ParseQueryString(returnToUri.Query).Get("state");
            if (!string.IsNullOrEmpty(stateParam))
            {
                callbackData.State = stateParam;
            }
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

        // Create user information with Steam ID
        var userInfo = await GetUserInfoAsync(steamId);

        return userInfo;
    }

    private Task<OidcUserInfo> GetUserInfoAsync(string steamId)
    {
        return Task.FromResult(new OidcUserInfo
        {
            UserId = steamId,
            Provider = ProviderName,
            // Steam OpenID doesn't provide additional user info
            Email = null,
            EmailVerified = false
        });
    }
}
