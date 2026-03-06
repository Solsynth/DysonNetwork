using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Padlock.Auth.OpenId;

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
    protected override string DiscoveryEndpoint => "https://steamcommunity.com/openid/.well-known/openid-configuration";
    protected override string ConfigSectionName => "Steam";

    public override async Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        var config = GetProviderConfig();

        var queryParams = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.realm"] = config.RedirectUri,
            ["openid.return_to"] = config.RedirectUri,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/claimed_id",
            ["openid.assoc_handle"] = "ASSOC_HANDLE"
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://steamcommunity.com/openid/login?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        return new OidcUserInfo { Provider = ProviderName };
    }
}
