using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Padlock.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Padlock.Auth.OpenId;

public abstract class OidcService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    AuthService auth,
    ICacheService cache,
    ActionLogService actionLogs
)
{
    protected readonly IConfiguration Configuration = configuration;
    protected readonly IHttpClientFactory HttpClientFactory = httpClientFactory;
    protected readonly AppDatabase Db = db;

    public abstract string ProviderName { get; }

    protected abstract string DiscoveryEndpoint { get; }

    protected abstract string ConfigSectionName { get; }

    public abstract Task<string> GetAuthorizationUrlAsync(string state, string nonce);

    public virtual string GetAuthorizationUrl(string state, string nonce)
    {
        return GetAuthorizationUrlAsync(state, nonce).GetAwaiter().GetResult();
    }

    protected Dictionary<string, string> BuildAuthorizationParameters(string clientId, string redirectUri, string scope, string responseType, string state, string nonce, string? responseMode = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = responseType,
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = nonce
        };

        if (!string.IsNullOrEmpty(responseMode))
        {
            parameters["response_mode"] = responseMode;
        }

        return parameters;
    }

    public abstract Task<OidcUserInfo> ProcessCallbackAsync(OidcCallbackData callbackData);

    protected ProviderConfiguration GetProviderConfig()
    {
        return new ProviderConfiguration
        {
            ClientId = Configuration[$"Oidc:{ConfigSectionName}:ClientId"] ?? "",
            ClientSecret = Configuration[$"Oidc:{ConfigSectionName}:ClientSecret"] ?? "",
            RedirectUri = Configuration["SiteUrl"] + "/auth/callback/" + ProviderName.ToLower()
        };
    }

    protected static string GenerateCodeVerifier()
    {
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);

        return Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    protected static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);

        return Convert.ToBase64String(hash)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    protected virtual async Task<OidcDiscoveryDocument?> GetDiscoveryDocumentAsync()
    {
        var cacheKey = $"oidc-discovery:{ProviderName}";

        var (found, cachedDoc) = await cache.GetAsyncWithStatus<OidcDiscoveryDocument>(cacheKey);
        if (found && cachedDoc != null)
        {
            return cachedDoc;
        }

        var client = HttpClientFactory.CreateClient();
        var response = await client.GetAsync(DiscoveryEndpoint);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<OidcDiscoveryDocument>();

        if (doc is not null)
            await cache.SetAsync(cacheKey, doc, TimeSpan.FromMinutes(15));

        return doc;
    }

    protected virtual async Task<OidcTokenResponse?> ExchangeCodeForTokensAsync(string code, string? codeVerifier = null)
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

    protected virtual Dictionary<string, string> BuildTokenRequestParameters(string code, ProviderConfiguration config, string? codeVerifier)
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

    protected virtual OidcUserInfo ValidateAndExtractIdToken(string idToken, TokenValidationParameters validationParameters)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(idToken, validationParameters, out _);

        var jwtToken = handler.ReadJwtToken(idToken);

        var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var emailVerified = jwtToken.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value == "true";
        var name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
        var givenName = jwtToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var familyName = jwtToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        var preferredUsername = jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        var picture = jwtToken.Claims.FirstOrDefault(c => c.Type == "picture")?.Value;

        var username = preferredUsername;
        if (string.IsNullOrEmpty(username))
        {
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
    /// Refreshes an access token using the refresh token
    /// </summary>
    public virtual async Task<OidcTokenResponse?> RefreshTokenAsync(string refreshToken)
        => throw new InvalidOperationException();

    public virtual async Task<string?> GetValidAccessTokenAsync(string refreshToken, string? currentAccessToken = null)
        => throw new InvalidOperationException();

    public async Task<SnAuthSession> CreateSessionForUserAsync(
        OidcUserInfo userInfo,
        SnAccount account,
        HttpContext request,
        string deviceId,
        string? deviceName = null,
        ClientPlatform platform = ClientPlatform.Web,
        SnAuthSession? parentSession = null
    )
    {
        var connection = await Db.AccountConnections
            .FirstOrDefaultAsync(c => c.Provider == ProviderName &&
                                      c.ProvidedIdentifier == userInfo.UserId &&
                                      c.AccountId == account.Id
            );

        if (connection is null)
        {
            connection = new SnAccountConnection
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

        var now = SystemClock.Instance.GetCurrentInstant();
        var device = await auth.GetOrCreateDeviceAsync(account.Id, deviceId, deviceName, platform);

        var session = new SnAuthSession
        {
            AccountId = account.Id,
            CreatedAt = now,
            LastGrantedAt = now,
            ParentSessionId = parentSession?.Id,
            ClientId = device.Id,
            ExpiredAt = now.Plus(Duration.FromDays(30))
        };

        await Db.AuthSessions.AddAsync(session);
        await Db.SaveChangesAsync();
        await actionLogs.CreateActionLogAsync(
            account.Id,
            ActionLogType.NewLogin,
            new Dictionary<string, object>
            {
                ["session_type"] = SessionType.Oidc.ToString(),
                ["provider"] = ProviderName
            },
            request.Request.Headers.UserAgent.ToString(),
            request.GetClientIpAddress(),
            null,
            session.Id
        );

        return session;
    }
}

public class ProviderConfiguration
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}

public class OidcDiscoveryDocument
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")] public string? TokenEndpoint { get; set; }

    [JsonPropertyName("userinfo_endpoint")]
    public string? UserinfoEndpoint { get; set; }

    [JsonPropertyName("jwks_uri")] public string? JwksUri { get; set; }
}

public class OidcTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")] public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }

    [JsonPropertyName("id_token")] public string? IdToken { get; set; }
}

public class OidcCallbackData
{
    public string Code { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string? State { get; set; }
    public string? RawData { get; set; }
    public Dictionary<string, string> QueryParameters { get; set; } = new();
}
