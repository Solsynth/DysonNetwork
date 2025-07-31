using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DysonNetwork.Pass.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DysonNetwork.Pass.Auth.OidcProvider.Services;
using DysonNetwork.Pass.Handlers;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Cache;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Pass.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
    public const string CookieTokenName = "AuthToken";
}

public enum TokenType
{
    AuthKey,
    ApiKey,
    OidcKey,
    Unknown
}

public class TokenInfo
{
    public string Token { get; set; } = string.Empty;
    public TokenType Type { get; set; } = TokenType.Unknown;
}

public class DysonTokenAuthOptions : AuthenticationSchemeOptions;

public class DysonTokenAuthHandler(
    IOptionsMonitor<DysonTokenAuthOptions> options,
    IConfiguration configuration,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDatabase database,
    OidcProviderService oidc,
    SubscriptionService subscriptions,
    ICacheService cache,
    FlushBufferService fbs
)
    : AuthenticationHandler<DysonTokenAuthOptions>(options, logger, encoder)
{
    public const string AuthCachePrefix = "auth:";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenInfo = _ExtractToken(Request);

        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
            return AuthenticateResult.Fail("No token was provided.");

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();

            // Validate token and extract session ID
            if (!ValidateToken(tokenInfo.Token, out var sessionId))
                return AuthenticateResult.Fail("Invalid token.");

            // Try to get session from cache first
            var session = await cache.GetAsync<AuthSession>($"{AuthCachePrefix}{sessionId}");

            // If not in cache, load from database
            if (session is null)
            {
                session = await database.AuthSessions
                    .Where(e => e.Id == sessionId)
                    .Include(e => e.Challenge)
                    .Include(e => e.Account)
                    .ThenInclude(e => e.Profile)
                    .FirstOrDefaultAsync();

                if (session is not null)
                {
                    var perk = await subscriptions.GetPerkSubscriptionAsync(session.AccountId);
                    session.Account.PerkSubscription = perk?.ToReference();

                    // Store in cache for future requests
                    await cache.SetWithGroupsAsync(
                        $"auth:{sessionId}",
                        session,
                        [$"{AccountService.AccountCachePrefix}{session.Account.Id}"],
                        TimeSpan.FromHours(1)
                    );
                }
            }

            // Check if the session exists
            if (session == null)
                return AuthenticateResult.Fail("Session not found.");

            // Check if the session is expired
            if (session.ExpiredAt.HasValue && session.ExpiredAt.Value < now)
                return AuthenticateResult.Fail("Session expired.");

            // Store user and session in the HttpContext.Items for easy access in controllers
            Context.Items["CurrentUser"] = session.Account;
            Context.Items["CurrentSession"] = session;
            Context.Items["CurrentTokenType"] = tokenInfo.Type.ToString();

            // Create claims from the session
            var claims = new List<Claim>
            {
                new("user_id", session.Account.Id.ToString()),
                new("session_id", session.Id.ToString()),
                new("token_type", tokenInfo.Type.ToString())
            };

            // Add scopes as claims
            session.Challenge.Scopes.ForEach(scope => claims.Add(new Claim("scope", scope)));

            // Add superuser claim if applicable
            if (session.Account.IsSuperuser)
                claims.Add(new Claim("is_superuser", "1"));

            // Create the identity and principal
            var identity = new ClaimsIdentity(claims, AuthConstants.SchemeName);
            var principal = new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(principal, AuthConstants.SchemeName);

            var lastInfo = new LastActiveInfo
            {
                Account = session.Account,
                Session = session,
                SeenAt = SystemClock.Instance.GetCurrentInstant(),
            };
            fbs.Enqueue(lastInfo);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private bool ValidateToken(string token, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        try
        {
            var parts = token.Split('.');

            switch (parts.Length)
            {
                // Handle JWT tokens (3 parts)
                case 3:
                {
                    var (isValid, jwtResult) = oidc.ValidateToken(token);
                    if (!isValid) return false;
                    var jti = jwtResult?.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
                    if (jti is null) return false;

                    return Guid.TryParse(jti, out sessionId);
                }
                // Handle compact tokens (2 parts)
                case 2:
                    // Original compact token validation logic
                    try
                    {
                        // Decode the payload
                        var payloadBytes = Base64UrlDecode(parts[0]);

                        // Extract session ID
                        sessionId = new Guid(payloadBytes);

                        // Load public key for verification
                        var publicKeyPem = File.ReadAllText(configuration["AuthToken:PublicKeyPath"]!);
                        using var rsa = RSA.Create();
                        rsa.ImportFromPem(publicKeyPem);

                        // Verify signature
                        var signature = Base64UrlDecode(parts[1]);
                        return rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    catch
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Token validation failed");
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private TokenInfo? _ExtractToken(HttpRequest request)
    {
        // Check for token in query parameters
        if (request.Query.TryGetValue(AuthConstants.TokenQueryParamName, out var queryToken))
        {
            return new TokenInfo
            {
                Token = queryToken.ToString(),
                Type = TokenType.AuthKey
            };
        }


        // Check for token in Authorization header
        var authHeader = request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                var parts = token.Split('.');

                return new TokenInfo
                {
                    Token = token,
                    Type = parts.Length == 3 ? TokenType.OidcKey : TokenType.AuthKey
                };
            }
            else if (authHeader.StartsWith("AtField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AtField ".Length..].Trim(),
                    Type = TokenType.AuthKey
                };
            }
            else if (authHeader.StartsWith("AkField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AkField ".Length..].Trim(),
                    Type = TokenType.ApiKey
                };
            }
        }

        // Check for token in cookies
        if (request.Cookies.TryGetValue(AuthConstants.CookieTokenName, out var cookieToken))
        {
            return new TokenInfo
            {
                Token = cookieToken,
                Type = cookieToken.Count(c => c == '.') == 2 ? TokenType.OidcKey : TokenType.AuthKey
            };
        }


        return null;
    }
}