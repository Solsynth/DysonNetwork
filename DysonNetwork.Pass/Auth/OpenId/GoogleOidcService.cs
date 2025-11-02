using System.IdentityModel.Tokens.Jwt;
using DysonNetwork.Shared.Cache;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Pass.Auth.OpenId;

public class GoogleOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
    : OidcService(configuration, httpClientFactory, db, auth, cache)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public override string ProviderName => "google";
    protected override string DiscoveryEndpoint => "https://accounts.google.com/.well-known/openid-configuration";
    protected override string ConfigSectionName => "Google";

    public override async Task<string> GetAuthorizationUrlAsync(string state, string nonce)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();

        if (discoveryDocument?.AuthorizationEndpoint == null)
        {
            throw new InvalidOperationException("Authorization endpoint not found in discovery document");
        }

        // Generate PKCE code verifier and challenge for enhanced security
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var queryParams = BuildAuthorizationParameters(
            config.ClientId,
            config.RedirectUri,
            "openid email profile",
            "code",
            state,
            nonce
        );

        // Add PKCE parameters
        queryParams["code_challenge"] = codeChallenge;
        queryParams["code_challenge_method"] = "S256";

        // Store code verifier in cache for later token exchange
        var codeVerifierKey = $"pkce:{state}";
        await cache.SetAsync(codeVerifierKey, codeVerifier, TimeSpan.FromMinutes(15));

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{discoveryDocument.AuthorizationEndpoint}?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        var state = callbackData.State ?? "";
        callbackData.State = state; // Keep the original state if needed

        // Retrieve PKCE code verifier from cache
        var codeVerifierKey = $"pkce:{state}";
        var (found, codeVerifier) = await cache.GetAsyncWithStatus<string>(codeVerifierKey);
        if (!found || string.IsNullOrEmpty(codeVerifier))
        {
            throw new InvalidOperationException("PKCE code verifier not found or expired");
        }

        // Remove the code verifier from cache to prevent replay attacks
        await cache.RemoveAsync(codeVerifierKey);

        // Exchange the code for tokens using PKCE
        var tokenResponse = await ExchangeCodeForTokensAsync(callbackData.Code, codeVerifier);
        if (tokenResponse == null)
        {
            throw new InvalidOperationException("Failed to exchange code for tokens");
        }

        // Use the strategy pattern to retrieve user info
        var discoveryDocument = await GetDiscoveryDocumentAsync();
        var config = GetProviderConfig();
        var strategy = new IdTokenValidationStrategy(_httpClientFactory);

        return await strategy.GetUserInfoAsync(tokenResponse, discoveryDocument, config.ClientId, ProviderName);
    }


}
