using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Pass.Auth.OpenId;

/// <summary>
/// Defines how to retrieve user information from an OIDC provider
/// </summary>
public interface IUserInfoStrategy
{
    /// <summary>
    /// Retrieves user information using the provided token response and discovery document
    /// </summary>
    Task<OidcUserInfo> GetUserInfoAsync(OidcTokenResponse tokenResponse, OidcDiscoveryDocument? discoveryDocument,
        string clientId, string providerName);
}

/// <summary>
/// Strategy for validating and extracting user info from ID tokens (Google, Apple)
/// </summary>
public class IdTokenValidationStrategy : IUserInfoStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;

    public IdTokenValidationStrategy(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OidcUserInfo> GetUserInfoAsync(OidcTokenResponse tokenResponse, OidcDiscoveryDocument? discoveryDocument,
        string clientId, string providerName)
    {
        if (string.IsNullOrEmpty(tokenResponse.IdToken))
            throw new InvalidOperationException("ID token not found in response");

        // Determine issuer and validation parameters based on provider
        var (issuer, jwksUri) = providerName.ToLower() switch
        {
            "google" => ("https://accounts.google.com",
                         discoveryDocument?.JwksUri ?? "https://www.googleapis.com/oauth2/v3/certs"),
            "apple" => ("https://appleid.apple.com",
                       "https://appleid.apple.com/auth/keys"),
            _ => throw new NotSupportedException($"ID token validation not supported for provider: {providerName}")
        };

        // Get and validate the token
        var jwksJson = await GetJwksAsync(jwksUri);
        var userInfo = await ValidateIdTokenAsync(tokenResponse.IdToken, clientId, issuer, jwksJson, providerName);

        // Set tokens on the user info
        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;

        // For Google, try to fetch additional profile data
        if (providerName.ToLower() == "google" && discoveryDocument?.UserinfoEndpoint != null
            && !string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            await FetchAdditionalProfileDataAsync(userInfo, discoveryDocument.UserinfoEndpoint,
                                                tokenResponse.AccessToken);
        }

        // For Apple, parse additional user data if provided
        if (providerName.ToLower() == "apple")
        {
            // Apple-specific handling would go here
        }

        return userInfo;
    }

    private async Task<string> GetJwksAsync(string jwksUri)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(jwksUri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<OidcUserInfo> ValidateIdTokenAsync(string idToken, string clientId, string issuer,
        string jwksJson, string providerName)
    {
        var jwks = JsonSerializer.Deserialize<JsonWebKeySet>(jwksJson)
            ?? throw new InvalidOperationException("Failed to parse JWKS");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(idToken);
        var kid = jwtToken.Header.Kid;

        var signingKey = jwks.Keys.FirstOrDefault(k => k.Kid == kid)
            ?? throw new SecurityTokenValidationException($"Unable to find key {kid} in JWKS");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            IssuerSigningKey = signingKey
        };

        handler.ValidateToken(idToken, validationParameters, out _);
        return ExtractUserInfoFromJwt(jwtToken, providerName);
    }

    private OidcUserInfo ExtractUserInfoFromJwt(JwtSecurityToken jwtToken, string providerName)
    {
        var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var emailVerified = jwtToken.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value == "true";
        var name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
        var givenName = jwtToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var familyName = jwtToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        var preferredUsername = jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        var picture = jwtToken.Claims.FirstOrDefault(c => c.Type == "picture")?.Value;

        // Determine preferred username - try different options
        var username = preferredUsername;
        if (string.IsNullOrEmpty(username))
        {
            // Fall back to email local part if no preferred username
            username = !string.IsNullOrEmpty(email) ? email.Split('@')[0] : null;
        }

        return new OidcUserInfo
        {
            UserId = userId,
            Email = email,
            EmailVerified = emailVerified,
            FirstName = givenName ?? "",
            LastName = familyName ?? "",
            DisplayName = name ?? $"{givenName} {familyName}".Trim(),
            PreferredUsername = username ?? "",
            ProfilePictureUrl = picture,
            Provider = providerName
        };
    }

    private async Task FetchAdditionalProfileDataAsync(OidcUserInfo userInfo, string userinfoEndpoint, string accessToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var userInfoResponse = await client.GetFromJsonAsync<Dictionary<string, object>>(userinfoEndpoint);
            if (userInfoResponse != null)
            {
                if (userInfoResponse.TryGetValue("picture", out var picture) && picture != null)
                {
                    userInfo.ProfilePictureUrl = picture.ToString();
                }
            }
        }
        catch
        {
            // Ignore errors when fetching additional profile data
        }
    }
}

/// <summary>
/// Strategy for fetching user info from OAuth 2.0 userinfo endpoints (Microsoft, Discord, GitHub)
/// </summary>
public class UserInfoEndpointStrategy : IUserInfoStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<JsonElement, OidcUserInfo> _parseUserInfo;
    private readonly string? _userAgent;

    public UserInfoEndpointStrategy(IHttpClientFactory httpClientFactory,
        Func<JsonElement, OidcUserInfo> parseUserInfo, string? userAgent = null)
    {
        _httpClientFactory = httpClientFactory;
        _parseUserInfo = parseUserInfo;
        _userAgent = userAgent;
    }

    public async Task<OidcUserInfo> GetUserInfoAsync(OidcTokenResponse tokenResponse, OidcDiscoveryDocument? discoveryDocument,
        string clientId, string providerName)
    {
        if (string.IsNullOrEmpty(tokenResponse.AccessToken) || string.IsNullOrEmpty(discoveryDocument?.UserinfoEndpoint))
            throw new InvalidOperationException("Access token or userinfo endpoint missing");

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, discoveryDocument.UserinfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
        if (!string.IsNullOrEmpty(_userAgent))
            request.Headers.Add("User-Agent", _userAgent);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var userElement = JsonDocument.Parse(json).RootElement;

        var userInfo = _parseUserInfo(userElement);
        userInfo.AccessToken = tokenResponse.AccessToken;
        userInfo.RefreshToken = tokenResponse.RefreshToken;
        userInfo.Provider = providerName;

        return userInfo;
    }
}

/// <summary>
/// Strategy for extracting user info directly from token responses (Afdian)
/// </summary>
public class DirectTokenResponseStrategy : IUserInfoStrategy
{
    public Task<OidcUserInfo> GetUserInfoAsync(OidcTokenResponse tokenResponse, OidcDiscoveryDocument? discoveryDocument,
        string clientId, string providerName)
    {
        // Parse user info directly from token response data
        // This would depend on how the specific provider returns user data
        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Access token missing");

        // For Afdian, the user data is embedded in the initial token response
        // This strategy would need to know how to parse that specific format

        var userInfo = new OidcUserInfo
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            Provider = providerName,
            // Parse user data from token response content...
        };

        return Task.FromResult(userInfo);
    }
}
