using System.Text;
using System.Security.Cryptography;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

public class TokenAuthService(
    AppDatabase db,
    ICacheService cache,
    ILogger<TokenAuthService> logger,
    OidcProvider.Services.OidcProviderService oidc,
    AuthTokenKeyProvider tokenKeyProvider
)
{
    public async Task<(bool Valid, SnAuthSession? Session, string? Message)> AuthenticateTokenAsync(string token, string? ipAddress = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("AuthenticateTokenAsync: no token provided");
                return (false, null, "No token provided.");
            }

            if (!string.IsNullOrEmpty(ipAddress))
            {
                logger.LogDebug("AuthenticateTokenAsync: client IP: {IpAddress}", ipAddress);
            }

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

            var cacheKey = $"{AuthCacheConstants.Prefix}{sessionId}";
            var session = await cache.GetAsync<SnAuthSession>(cacheKey);
            if (session is not null)
            {
                logger.LogDebug("AuthenticateTokenAsync: cache hit for {CacheKey}", cacheKey);
                var nowHit = SystemClock.Instance.GetCurrentInstant();
                if (session.ExpiredAt.HasValue && session.ExpiredAt < nowHit)
                {
                    logger.LogWarning("AuthenticateTokenAsync: cached session expired (sessionId={SessionId})", sessionId);
                    await cache.RemoveAsync(cacheKey);
                    return (false, null, "Session has been expired.");
                }
                logger.LogInformation(
                    "AuthenticateTokenAsync: success via cache (sessionId={SessionId}, accountId={AccountId}, scopes={ScopeCount}, expiresAt={ExpiresAt})",
                    sessionId,
                    session.AccountId,
                    session.Scopes.Count,
                    session.ExpiredAt
                );
                return (true, session, null);
            }

            logger.LogDebug("AuthenticateTokenAsync: cache miss for {CacheKey}, loading from DB", cacheKey);

            session = await db.AuthSessions
                .AsNoTracking()
                .Include(e => e.Client)
                .Include(e => e.Account)
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
                session.ClientId,
                session.AppId,
                session.Scopes.Count,
                session.IpAddress,
                (session.UserAgent ?? string.Empty).Length
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
                session.ClientId
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
                        var (isValid, jwtResult) = oidc.ValidateToken(token);
                        if (!isValid) return false;
                        var jti = jwtResult?.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
                        if (jti is null) return false;
                        return Guid.TryParse(jti, out sessionId);
                    }
                case 2:
                    {
                        return tokenKeyProvider.TryValidateCompactToken(token, out sessionId);
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
}
