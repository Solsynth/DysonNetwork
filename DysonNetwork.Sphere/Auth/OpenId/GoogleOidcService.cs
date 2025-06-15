using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// Implementation of OpenID Connect service for Google Sign In
/// </summary>
public class GoogleOidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db
)
    : OidcService(configuration, httpClientFactory, db)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    
    public override string ProviderName => "google";
    protected override string DiscoveryEndpoint => "https://accounts.google.com/.well-known/openid-configuration";
    protected override string ConfigSectionName => "Google";

    public override string GetAuthorizationUrl(string state, string nonce)
    {
        var config = GetProviderConfig();
        var discoveryDocument = GetDiscoveryDocumentAsync().GetAwaiter().GetResult();

        if (discoveryDocument?.AuthorizationEndpoint == null)
        {
            throw new InvalidOperationException("Authorization endpoint not found in discovery document");
        }

        // Generate code verifier and challenge for PKCE
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Store code verifier in session or cache for later use
        // For simplicity, we'll append it to the state parameter in this example
        var combinedState = $"{state}|{codeVerifier}";

        var queryParams = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "redirect_uri", config.RedirectUri },
            { "response_type", "code" },
            { "scope", "openid email profile" },
            { "state", combinedState },
            { "nonce", nonce },
            { "code_challenge", codeChallenge },
            { "code_challenge_method", "S256" }
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{discoveryDocument.AuthorizationEndpoint}?{queryString}";
    }

    public override async Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData)
    {
        // Extract code verifier from state
        string? codeVerifier = null;
        var state = callbackData.State ?? "";

        if (state.Contains('|'))
        {
            var parts = state.Split('|');
            state = parts[0];
            codeVerifier = parts.Length > 1 ? parts[1] : null;
            callbackData.State = state; // Set the clean state back
        }

        // Exchange the code for tokens
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

                var userInfoResponse = await client.GetFromJsonAsync<Dictionary<string, object>>(discoveryDocument.UserinfoEndpoint);

                if (userInfoResponse != null)
                {
                    // Extract any additional fields that might be available
                    if (userInfoResponse.TryGetValue("picture", out var picture) && picture != null)
                    {
                        userInfo.ProfilePictureUrl = picture.ToString();
                    }
                }
            }
        }
        catch (Exception)
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

        // Get Google's signing keys
        var client = _httpClientFactory.CreateClient();
        var jwksResponse = await client.GetFromJsonAsync<JsonWebKeySet>(discoveryDocument.JwksUri);
        if (jwksResponse == null)
        {
            throw new InvalidOperationException("Failed to retrieve JWKS from Google");
        }

        // Parse the JWT to get the key ID
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(idToken);
        var kid = jwtToken.Header.Kid;

        // Find the matching key
        var signingKey = jwksResponse.Keys.FirstOrDefault(k => k.Kid == kid);
        if (signingKey == null)
        {
            throw new SecurityTokenValidationException("Unable to find matching key in Google's JWKS");
        }

        // Create validation parameters
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

    #region PKCE Support

    public string GenerateCodeVerifier()
    {
        var randomBytes = new byte[32]; // 256 bits
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(randomBytes);

        return Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(challengeBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    #endregion
}