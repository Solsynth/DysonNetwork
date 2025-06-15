using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// Base service for OpenID Connect authentication providers
/// </summary>
public abstract class OidcService(IConfiguration configuration, IHttpClientFactory httpClientFactory, AppDatabase db)
{
    /// <summary>
    /// Gets the unique identifier for this provider
    /// </summary>
    public abstract string ProviderName { get; }

    /// <summary>
    /// Gets the OIDC discovery document endpoint
    /// </summary>
    protected abstract string DiscoveryEndpoint { get; }

    /// <summary>
    /// Gets configuration section name for this provider
    /// </summary>
    protected abstract string ConfigSectionName { get; }

    /// <summary>
    /// Gets the authorization URL for initiating the authentication flow
    /// </summary>
    public abstract string GetAuthorizationUrl(string state, string nonce);

    /// <summary>
    /// Process the callback from the OIDC provider
    /// </summary>
    public abstract Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData);

    /// <summary>
    /// Gets the provider configuration
    /// </summary>
    protected ProviderConfiguration GetProviderConfig()
    {
        return new ProviderConfiguration
        {
            ClientId = configuration[$"Oidc:{ConfigSectionName}:ClientId"] ?? "",
            ClientSecret = configuration[$"Oidc:{ConfigSectionName}:ClientSecret"] ?? "",
            RedirectUri = configuration[$"Oidc:{ConfigSectionName}:RedirectUri"] ?? ""
        };
    }

    /// <summary>
    /// Retrieves the OpenID Connect discovery document
    /// </summary>
    protected async Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync(DiscoveryEndpoint);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OidcDiscoveryDocument>();
    }

    /// <summary>
    /// Exchange the authorization code for tokens
    /// </summary>
    protected async Task<OidcTokenResponse?> ExchangeCodeForTokensAsync(string code, string? codeVerifier = null)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();

        if (discoveryDocument?.TokenEndpoint == null)
        {
            throw new InvalidOperationException("Token endpoint not found in discovery document");
        }

        var client = httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(BuildTokenRequestParameters(code, config, codeVerifier));

        var response = await client.PostAsync(discoveryDocument.TokenEndpoint, content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<OidcTokenResponse>();
    }

    /// <summary>
    /// Build the token request parameters
    /// </summary>
    protected virtual Dictionary<string, string> BuildTokenRequestParameters(string code, ProviderConfiguration config,
        string? codeVerifier)
    {
        var parameters = new Dictionary<string, string>
        {
            { "client_id", config.ClientId },
            { "code", code },
            { "grant_type", "authorization_code" },
            { "redirect_uri", config.RedirectUri }
        };

        if (!string.IsNullOrEmpty(config.ClientSecret))
        {
            parameters.Add("client_secret", config.ClientSecret);
        }

        if (!string.IsNullOrEmpty(codeVerifier))
        {
            parameters.Add("code_verifier", codeVerifier);
        }

        return parameters;
    }

    /// <summary>
    /// Validates and extracts information from an ID token
    /// </summary>
    protected virtual OidcUserInfo ValidateAndExtractIdToken(string idToken,
        TokenValidationParameters validationParameters)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(idToken, validationParameters, out _);

        var jwtToken = handler.ReadJwtToken(idToken);

        // Extract standard claims
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
            Provider = ProviderName
        };
    }

    /// <summary>
    /// Creates a challenge and session for an authenticated user
    /// Also creates or updates the account connection
    /// </summary>
    public async Task<Session> CreateSessionForUserAsync(OidcUserInfo userInfo, Account.Account account)
    {
        // Create or update the account connection
        var connection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.Provider == ProviderName &&
                                      c.ProvidedIdentifier == userInfo.UserId &&
                                      c.AccountId == account.Id
            );

        if (connection is null)
        {
            connection = new AccountConnection
            {
                Provider = ProviderName,
                ProvidedIdentifier = userInfo.UserId ?? "",
                AccessToken = userInfo.AccessToken,
                RefreshToken = userInfo.RefreshToken,
                LastUsedAt = NodaTime.SystemClock.Instance.GetCurrentInstant(),
                AccountId = account.Id
            };
            await db.AccountConnections.AddAsync(connection);
        }

        // Create a challenge that's already completed
        var now = SystemClock.Instance.GetCurrentInstant();
        var challenge = new Challenge
        {
            ExpiredAt = now.Plus(Duration.FromHours(1)),
            StepTotal = 1,
            StepRemain = 0, // Already verified by provider
            Platform = ChallengePlatform.Unidentified,
            Audiences = [ProviderName],
            Scopes = ["*"],
            AccountId = account.Id
        };

        await db.AuthChallenges.AddAsync(challenge);

        // Create a session
        var session = new Session
        {
            LastGrantedAt = now,
            Account = account,
            Challenge = challenge,
        };

        await db.AuthSessions.AddAsync(session);
        await db.SaveChangesAsync();

        return session;
    }
}

/// <summary>
/// Provider configuration from app settings
/// </summary>
public class ProviderConfiguration
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}

/// <summary>
/// OIDC Discovery Document
/// </summary>
public class OidcDiscoveryDocument
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")] public string? TokenEndpoint { get; set; }

    [JsonPropertyName("userinfo_endpoint")]
    public string? UserinfoEndpoint { get; set; }

    [JsonPropertyName("jwks_uri")] public string? JwksUri { get; set; }
}

/// <summary>
/// Response from the token endpoint
/// </summary>
public class OidcTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")] public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }

    [JsonPropertyName("id_token")] public string? IdToken { get; set; }
}

/// <summary>
/// Data received in the callback from an OIDC provider
/// </summary>
public class OidcCallbackData
{
    public string Code { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string? State { get; set; }
    public string? CodeVerifier { get; set; }
    public string? RawData { get; set; }
}