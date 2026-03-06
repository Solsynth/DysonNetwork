using DysonNetwork.Shared.Cache;

namespace DysonNetwork.Padlock.Auth.OpenId;

public class GitHubOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    public override string ProviderName => "github";
    protected override string DiscoveryEndpoint => "https://github.com/.well-known/openid-configuration";
    protected override string ConfigSectionName => "GitHub";

    public override async Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();

        if (discoveryDocument?.AuthorizationEndpoint == null)
            throw new InvalidOperationException("Authorization endpoint not found");

        var queryParams = BuildAuthorizationParameters(
            config.ClientId,
            config.RedirectUri,
            "read:user user:email",
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
}
