using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Auth;

public class TokenAuthService(
    AppDatabase db,
    IConfiguration config,
    ICacheService cache,
    ILogger<TokenAuthService> logger,
    OidcProvider.Services.OidcProviderService oidc,
    SubscriptionService subscriptions
)
{
    /// <summary>
    /// Universal authenticate method: validate token (JWT or compact),
    /// load session from cache/DB, check expiry, enrich with subscription,
    /// then cache and return.
    /// </summary>
    /// <param name="token">Incoming token string</param>
    /// <returns>(Valid, Session, Message)</returns>
    public async Task<(bool Valid, AuthSession? Session, string? Message)> AuthenticateTokenAsync(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("AuthenticateTokenAsync: no token provided");
                return (false, null, "No token provided.");
            }

            // token fingerprint for correlation
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            var tokenFp = tokenHash[..8];

            var partsLen = token.Split('.').Length;
            var format = partsLen switch
            {
                3 => "JWT",
                2 => "Compact",
                _ => "Unknown"
            };
            logger.LogDebug("AuthenticateTokenAsync: token format detected: {Format} (fp={TokenFp})", format, tokenFp);

            if (!ValidateToken(token, out var sessionId))
            {
                logger.LogWarning("AuthenticateTokenAsync: token validation failed (format={Format}, fp={TokenFp})", format, tokenFp);
                return (false, null, "Invalid token.");
            }

            logger.LogDebug("AuthenticateTokenAsync: token validated, sessionId={SessionId} (fp={TokenFp})", sessionId, tokenFp);

            // Try cache first
            var cacheKey = $"{AuthCacheConstants.Prefix}{sessionId}";
            var session = await cache.GetAsync<AuthSession>(cacheKey);
            if (session is not null)
            {
                logger.LogDebug("AuthenticateTokenAsync: cache hit for {CacheKey}", cacheKey);
                var nowHit = SystemClock.Instance.GetCurrentInstant();
                if (session.ExpiredAt.HasValue && session.ExpiredAt < nowHit)
                {
                    logger.LogWarning("AuthenticateTokenAsync: cached session expired (sessionId={SessionId})", sessionId);
                    return (false, null, "Session has been expired.");
                }
                logger.LogInformation(
                    "AuthenticateTokenAsync: success via cache (sessionId={SessionId}, accountId={AccountId}, scopes={ScopeCount}, expiresAt={ExpiresAt})",
                    sessionId,
                    session.AccountId,
                    session.Challenge.Scopes.Count,
                    session.ExpiredAt
                );
                return (true, session, null);
            }

            logger.LogDebug("AuthenticateTokenAsync: cache miss for {CacheKey}, loading from DB", cacheKey);

            session = await db.AuthSessions
                .AsNoTracking()
                .Include(e => e.Challenge)
                .ThenInclude(e => e.Client)
                .Include(e => e.Account)
                .ThenInclude(e => e.Profile)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session is null)
            {
                logger.LogWarning("AuthenticateTokenAsync: session not found (sessionId={SessionId})", sessionId);
                return (false, null, "Session was not found.");
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            if (session.ExpiredAt.HasValue && session.ExpiredAt < now)
            {
                logger.LogWarning("AuthenticateTokenAsync: session expired (sessionId={SessionId}, expiredAt={ExpiredAt}, now={Now})", sessionId, session.ExpiredAt, now);
                return (false, null, "Session has been expired.");
            }

            logger.LogInformation(
                "AuthenticateTokenAsync: DB session loaded (sessionId={SessionId}, accountId={AccountId}, clientId={ClientId}, appId={AppId}, scopes={ScopeCount}, ip={Ip}, uaLen={UaLen})",
                sessionId,
                session.AccountId,
                session.Challenge.ClientId,
                session.AppId,
                session.Challenge.Scopes.Count,
                session.Challenge.IpAddress,
                (session.Challenge.UserAgent ?? string.Empty).Length
            );

            logger.LogDebug("AuthenticateTokenAsync: enriching account with subscription (accountId={AccountId})", session.AccountId);
            var perk = await subscriptions.GetPerkSubscriptionAsync(session.AccountId);
            session.Account.PerkSubscription = perk?.ToReference();
            logger.LogInformation(
                "AuthenticateTokenAsync: subscription attached (accountId={AccountId}, hasPerk={HasPerk}, identifier={Identifier}, status={Status}, available={Available})",
                session.AccountId,
                perk is not null,
                perk?.Identifier,
                perk?.Status,
                perk?.IsAvailable
            );

            await cache.SetWithGroupsAsync(
                cacheKey,
                session,
                [$"{AuthCacheConstants.Prefix}{session.Account.Id}"],
                TimeSpan.FromHours(1)
            );
            logger.LogDebug("AuthenticateTokenAsync: cached session with key {CacheKey} (groups=[{GroupKey}])",
                cacheKey,
                $"{AuthCacheConstants.Prefix}{session.Account.Id}");

            logger.LogInformation(
                "AuthenticateTokenAsync: success via DB (sessionId={SessionId}, accountId={AccountId}, clientId={ClientId})",
                sessionId,
                session.AccountId,
                session.Challenge.ClientId
            );
            return (true, session, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AuthenticateTokenAsync: unexpected error");
            return (false, null, "Authentication error.");
        }
    }
    
    public bool ValidateToken(string token, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        try
        {
            var parts = token.Split('.');

            switch (parts.Length)
            {
                case 3:
                {
                    // JWT via OIDC
                    var (isValid, jwtResult) = oidc.ValidateToken(token);
                    if (!isValid) return false;
                    var jti = jwtResult?.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
                    if (jti is null) return false;
                    return Guid.TryParse(jti, out sessionId);
                }
                case 2:
                {
                    // Compact token
                    var payloadBytes = Base64UrlDecode(parts[0]);
                    sessionId = new Guid(payloadBytes);

                    var publicKeyPem = File.ReadAllText(config["AuthToken:PublicKeyPath"]!);
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(publicKeyPem);

                    var signature = Base64UrlDecode(parts[1]);
                    return rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                default:
                    return false;
            }
        }
        catch
        {
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
}
