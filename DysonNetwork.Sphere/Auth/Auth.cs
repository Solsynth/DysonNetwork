using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Storage.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Sphere.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
}

public enum TokenType
{
    AuthKey,
    ApiKey,
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
            var session = await cache.GetAsync<Session>($"{AuthCachePrefix}{sessionId}");

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
                    // Store in cache for future requests
                    await cache.SetWithGroupsAsync(
                        $"Auth_{sessionId}",
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
            // Split the token
            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;

            // Decode the payload
            var payloadBytes = Base64UrlDecode(parts[0]);

            // Extract session ID
            sessionId = new Guid(payloadBytes);

            // Load public key for verification
            var publicKeyPem = File.ReadAllText(configuration["Jwt:PublicKeyPath"]!);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            // Verify signature
            var signature = Base64UrlDecode(parts[1]);
            return rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        string padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static TokenInfo? _ExtractToken(HttpRequest request)
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
                return new TokenInfo
                {
                    Token = authHeader["Bearer ".Length..].Trim(),
                    Type = TokenType.AuthKey
                };
            }
            
            if (authHeader.StartsWith("AtField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AtField ".Length..].Trim(),
                    Type = TokenType.AuthKey
                };
            }

            if (authHeader.StartsWith("AkField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AkField ".Length..].Trim(),
                    Type = TokenType.ApiKey
                };
            }
        }

        // Check for token in cookies
        if (request.Cookies.TryGetValue(AuthConstants.TokenQueryParamName, out var cookieToken))
        {
            return new TokenInfo
            {
                Token = cookieToken,
                Type = TokenType.AuthKey
            };
        }

        return null;
    }
}