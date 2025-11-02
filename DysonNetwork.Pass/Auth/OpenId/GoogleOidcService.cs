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
        if (tokenResponse?.IdToken == null)
        {
            throw new InvalidOperationException("Failed to obtain ID token from Google");
        }

        // Validate the ID token
        var userInfo = await ValidateTokenAsync(tokenResponse.IdToken);

        // Set tokens on the user info
        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;

        // Try to fetch additional profile data if userinfo endpoint is available
        try
        {
            var discoveryDocument = await GetDiscoveryDocumentAsync();
            if (discoveryDocument?.UserinfoEndpoint != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);

                var userInfoResponse =
                    await client.GetFromJsonAsync<Dictionary<string, object>>(discoveryDocument.UserinfoEndpoint);

                if (userInfoResponse != null)
                {
                    if (userInfoResponse.TryGetValue("picture", out var picture) && picture != null)
                    {
                        userInfo.ProfilePictureUrl = picture.ToString();
                    }
                }
            }
        }
        catch
        {
            // Ignore errors when fetching additional profile data
        }

        return userInfo;
    }

    private async Task<OidcUserInfo> ValidateTokenAsync(string idToken)
    {
        var discoveryDocument = await GetDiscoveryDocumentAsync();
        if (discoveryDocument?.JwksUri == null)
        {
            throw new InvalidOperationException("JWKS URI not found in discovery document");
        }

        var client = _httpClientFactory.CreateClient();
        var jwksResponse = await client.GetFromJsonAsync<JsonWebKeySet>(discoveryDocument.JwksUri);
        if (jwksResponse == null)
        {
            throw new InvalidOperationException("Failed to retrieve JWKS from Google");
        }

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(idToken);
        var kid = jwtToken.Header.Kid;
        var signingKey = jwksResponse.Keys.FirstOrDefault(k => k.Kid == kid);
        if (signingKey == null)
        {
            throw new SecurityTokenValidationException("Unable to find matching key in Google's JWKS");
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://accounts.google.com",
            ValidateAudience = true,
            ValidAudience = GetProviderConfig().ClientId,
            ValidateLifetime = true,
            IssuerSigningKey = signingKey
        };

        return ValidateAndExtractIdToken(idToken, validationParameters);
    }
}
