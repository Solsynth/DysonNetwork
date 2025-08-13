using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Pass.Auth.OpenId;

/// <summary>
/// Base service for OpenID Connect authentication providers
/// </summary>
public abstract class OidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache
)
{
    protected readonly IConfiguration Configuration = configuration;
    protected readonly IHttpClientFactory HttpClientFactory = httpClientFactory;
    protected readonly AppDatabase Db = db;

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
            ClientId = Configuration[$"Oidc:{ConfigSectionName}:ClientId"] ?? "",
            ClientSecret = Configuration[$"Oidc:{ConfigSectionName}:ClientSecret"] ?? "",
            RedirectUri = Configuration["BaseUrl"] + "/auth/callback/" + ProviderName.ToLower()
        };
    }

    /// <summary>
    /// Retrieves the OpenID Connect discovery document
    /// </summary>
    protected virtual async Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        // Construct a cache key unique to the current provider:
        var cacheKey = $"oidc-discovery:{ProviderName}";

        // Try getting the discovery document from cache first:
        var (found, cachedDoc) = await cache.GetAsyncWithStatus<OidcDiscoveryDocument>(cacheKey);
        if (found && cachedDoc != null)
        {
            return cachedDoc;
        }

        // If it's not cached, fetch from the actual discovery endpoint:
        var client = HttpClientFactory.CreateClient();
        var response = await client.GetAsync(DiscoveryEndpoint);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<OidcDiscoveryDocument>();

        // Store the discovery document in the cache for a while (e.g., 15 minutes):
        if (doc is not null)
            await cache.SetAsync(cacheKey, doc, TimeSpan.FromMinutes(15));

        return doc;

    }

    /// <summary>
    /// Exchange the authorization code for tokens
    /// </summary>
    protected virtual async Task<OidcTokenResponse?> ExchangeCodeForTokensAsync(string code,
        string? codeVerifier = null)
    {
        var config = GetProviderConfig();
        var discoveryDocument = await GetDiscoveryDocumentAsync();

        if (discoveryDocument?.TokenEndpoint == null)
        {
            throw new InvalidOperationException("Token endpoint not found in discovery document");
        }

        var client = HttpClientFactory.CreateClient();
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
    public async Task<AuthChallenge> CreateChallengeForUserAsync(
        OidcUserInfo userInfo,
        Account.Account account,
        HttpContext request,
        string deviceId,
        string? deviceName = null
    )
    {
        // Create or update the account connection
        var connection = await Db.AccountConnections
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
                LastUsedAt = SystemClock.Instance.GetCurrentInstant(),
                AccountId = account.Id
            };
            await Db.AccountConnections.AddAsync(connection);
        }

        // Create a challenge that's already completed
        var now = SystemClock.Instance.GetCurrentInstant();
        var device = await auth.GetOrCreateDeviceAsync(account.Id, deviceId, deviceName, ClientPlatform.Ios);
        var challenge = new AuthChallenge
        {
            ExpiredAt = now.Plus(Duration.FromHours(1)),
            StepTotal = await auth.DetectChallengeRisk(request.Request, account),
            Type = ChallengeType.Oidc,
            Audiences = [ProviderName],
            Scopes = ["*"],
            AccountId = account.Id,
            ClientId = device.Id,
            IpAddress = request.Connection.RemoteIpAddress?.ToString() ?? null,
            UserAgent = request.Request.Headers.UserAgent,
        };
        challenge.StepRemain--;
        if (challenge.StepRemain < 0) challenge.StepRemain = 0;

        await Db.AuthChallenges.AddAsync(challenge);
        await Db.SaveChangesAsync();

        return challenge;
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
    public string? RawData { get; set; }
}