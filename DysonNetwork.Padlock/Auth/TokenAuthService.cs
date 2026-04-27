using System.Text;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Auth;

public class TokenAuthService(
    AppDatabase db,
    ICacheService cache,
    ILogger<TokenAuthService> logger,
    OidcProvider.Services.OidcProviderService oidc,
    AuthTokenKeyProvider tokenKeyProvider,
    AuthJwtService authJwt,
    RemoteSubscriptionService subscriptions,
    IConfiguration config
) 
{
    private static readonly DateTime LegacyDefaultCutoffUtc = DateTime.UtcNow.AddDays(14);
    public async Task<(bool Valid, SnAuthSession? Session, string? Message, string? TokenUse)> AuthenticateTokenAsync(string token, string? ipAddress = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogDebug("AuthenticateTokenAsync: no token provided");
                return (false, null, "No token provided.", null);
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

            JwtSecurityToken? oidcJwt = null;
            var authPath = "primary";
            if (!ValidateToken(token, out var sessionId, out var tokenUse, out var tokenEpoch))
            {
                var (isOidcToken, oidcToken, oidcSessionId, oidcTokenUse, oidcEpoch) = ValidateOidcToken(token);
                if (!isOidcToken || oidcToken is null || !oidcSessionId.HasValue)
                {
                    logger.LogInformation("AuthenticateTokenAsync: token validation failed (format={Format}, fp={TokenFp})", format, tokenFp);
                    return (false, null, "Invalid token.", null);
                }

                oidcJwt = oidcToken;
                authPath = "oidc_fallback";
                sessionId = oidcSessionId.Value;
                tokenUse = oidcTokenUse;
                tokenEpoch = oidcEpoch;
            }
            if (string.Equals(tokenUse, "refresh", StringComparison.Ordinal))
                return (false, null, "Refresh token cannot be used for authentication.", tokenUse);

            logger.LogDebug("AuthenticateTokenAsync: token validated, sessionId={SessionId} (fp={TokenFp})", sessionId, tokenFp);

            var cacheKey = AuthCacheConstants.Session(sessionId.ToString());
            var cachedSession = await cache.GetAsync<DyAuthSession>(cacheKey);
            if (cachedSession is not null)
            {
                logger.LogDebug("AuthenticateTokenAsync: cache hit for {CacheKey}", cacheKey);
                var cachedSnSession = SnAuthSession.FromProtoValue(cachedSession);

                // Validate epoch (treat missing epoch as 0 for backward compatibility)
                var effectiveTokenEpoch = tokenEpoch ?? 0;
                if (cachedSnSession.Epoch != effectiveTokenEpoch)
                {
                    logger.LogInformation("AuthenticateTokenAsync: epoch mismatch (sessionId={SessionId}, tokenEpoch={TokenEpoch}, sessionEpoch={SessionEpoch})",
                        sessionId, effectiveTokenEpoch, cachedSnSession.Epoch);
                    await cache.RemoveAsync(cacheKey);
                    return (false, null, "Token has been invalidated.", null);
                }

                var nowHit = SystemClock.Instance.GetCurrentInstant();
                if (cachedSnSession.ExpiredAt.HasValue && cachedSnSession.ExpiredAt < nowHit)
                {
                    logger.LogInformation("AuthenticateTokenAsync: cached session expired (sessionId={SessionId})", sessionId);
                    await cache.RemoveAsync(cacheKey);
                    return (false, null, "Session has been expired.", null);
                }
                logger.LogInformation(
                    "AuthenticateTokenAsync: success via cache (sessionId={SessionId}, accountId={AccountId}, scopes={ScopeCount}, expiresAt={ExpiresAt}, path={Path})",
                    sessionId,
                    cachedSnSession.AccountId,
                    cachedSnSession.Scopes.Count,
                    cachedSnSession.ExpiredAt,
                    authPath
                );

                if (oidcJwt is not null)
                {
                    var (bound, message) = await ValidateOidcBindingAsync(cachedSnSession, oidcJwt);
                    if (!bound)
                    {
                        logger.LogInformation(
                            "AuthenticateTokenAsync: OIDC token binding failed for cached session (sessionId={SessionId}, reason={Reason})",
                            sessionId,
                            message
                        );
                        return (false, null, message, tokenUse);
                    }
                }

                return (true, cachedSnSession, null, tokenUse);
            }

            logger.LogDebug("AuthenticateTokenAsync: cache miss for {CacheKey}, loading from DB", cacheKey);

            var session = await db.AuthSessions
                .AsNoTracking()
                .Include(e => e.Client)
                .Include(e => e.Account)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session is null)
            {
                logger.LogInformation("AuthenticateTokenAsync: session not found (sessionId={SessionId})", sessionId);
                return (false, null, "Session was not found.", null);
            }

            // Validate epoch (treat missing epoch as 0 for backward compatibility)
            var effectiveTokenEpoch2 = tokenEpoch ?? 0;
            if (session.Epoch != effectiveTokenEpoch2)
            {
                logger.LogInformation("AuthenticateTokenAsync: epoch mismatch (sessionId={SessionId}, tokenEpoch={TokenEpoch}, sessionEpoch={SessionEpoch})",
                    sessionId, effectiveTokenEpoch2, session.Epoch);
                return (false, null, "Token has been invalidated.", null);
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            if (session.ExpiredAt.HasValue && session.ExpiredAt < now)
            {
                logger.LogInformation("AuthenticateTokenAsync: session expired (sessionId={SessionId}, expiredAt={ExpiredAt}, now={Now})", sessionId, session.ExpiredAt, now);
                return (false, null, "Session has been expired.", null);
            }

            logger.LogInformation(
                "AuthenticateTokenAsync: DB session loaded (sessionId={SessionId}, accountId={AccountId}, clientId={ClientId}, appId={AppId}, scopes={ScopeCount}, ip={Ip}, uaLen={UaLen})",
                sessionId,
                session.AccountId,
                session.ClientId,
                session.AppId,
                session.Scopes.Count,
                session.IpAddress ?? "null",
                (session.UserAgent ?? string.Empty).Length
            );

            // Hydrate PerkLevel for the account
            await HydratePerkAsync(session.Account);

            await cache.SetWithGroupsAsync(
                cacheKey,
                session.ToProtoValue(),
                [$"auth:account_sessions:{session.Account.Id}"],
                TimeSpan.FromHours(1)
            );
            logger.LogDebug("AuthenticateTokenAsync: cached session with key {CacheKey} (groups=[{GroupKey}])",
                cacheKey,
                $"auth:account_sessions:{session.Account.Id}");

            logger.LogInformation(
                "AuthenticateTokenAsync: success via DB (sessionId={SessionId}, accountId={AccountId}, clientId={ClientId}, path={Path})",
                sessionId,
                session.AccountId,
                session.ClientId,
                authPath
            );

            if (oidcJwt is not null)
            {
                var (bound, message) = await ValidateOidcBindingAsync(session, oidcJwt);
                if (!bound)
                {
                    logger.LogInformation(
                        "AuthenticateTokenAsync: OIDC token binding failed for DB session (sessionId={SessionId}, reason={Reason})",
                        sessionId,
                        message
                    );
                    return (false, null, message, tokenUse);
                }
            }

            return (true, session, null, tokenUse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AuthenticateTokenAsync: unexpected error");
            return (false, null, "Authentication error.", null);
        }
    }

    private (bool IsValid, JwtSecurityToken? Token, Guid? SessionId, string? TokenUse, int? Epoch) ValidateOidcToken(string token)
    {
        var (isValid, jwt) = oidc.ValidateToken(token);
        if (!isValid || jwt is null)
        {
            var (relaxedValid, relaxedJwt, reason) = ValidateOidcTokenRelaxed(token);
            if (!relaxedValid || relaxedJwt is null)
            {
                logger.LogInformation("AuthenticateTokenAsync: OIDC fallback validation failed. reason={Reason}", reason ?? "unknown");
                return (false, null, null, null, null);
            }

            jwt = relaxedJwt;
            logger.LogWarning(
                "AuthenticateTokenAsync: accepted OIDC token with relaxed issuer validation. iss={Issuer} aud={Audience}",
                jwt.Issuer,
                string.Join(",", jwt.Audiences)
            );
        }

        var jti = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        var sessionIdText = jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value ?? jti;
        if (!Guid.TryParse(sessionIdText, out var sessionId))
            return (false, null, null, null, null);

        var tokenUse = jwt.Claims.FirstOrDefault(c => c.Type == AuthJwtService.ClaimType)?.Value
                       ?? jwt.Claims.FirstOrDefault(c => c.Type == AuthJwtService.LegacyClaimTokenUse)?.Value
                       ?? "user";

        int? epoch = null;
        var epochClaim = jwt.Claims.FirstOrDefault(c => c.Type == "epoch")?.Value;
        if (int.TryParse(epochClaim, out var parsedEpoch))
            epoch = parsedEpoch;

        return (true, jwt, sessionId, tokenUse, epoch);
    }

    private (bool IsValid, JwtSecurityToken? Token, string? Reason) ValidateOidcTokenRelaxed(string token)
    {
        try
        {
            var keyPath = config["OidcProvider:PublicKeyPath"] ?? config["AuthToken:PublicKeyPath"];
            if (string.IsNullOrWhiteSpace(keyPath))
                return (false, null, "OIDC public key path is not configured");

            if (!File.Exists(keyPath))
                return (false, null, $"OIDC public key not found at {keyPath}");

            var pem = File.ReadAllText(keyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);

            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(rsa),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };

            handler.ValidateToken(token, parameters, out var validatedToken);
            return (true, validatedToken as JwtSecurityToken, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<(bool Valid, string? Message)> ValidateOidcBindingAsync(SnAuthSession session, JwtSecurityToken jwt)
    {
        if (!session.AppId.HasValue || session.Type != SessionType.OAuth)
            return (false, "OIDC token is not bound to an OAuth session.");

        var app = await oidc.FindClientByIdAsync(session.AppId.Value);
        if (app is null || string.IsNullOrWhiteSpace(app.Slug))
            return (false, "OIDC client is not found for this session.");

        var audienceMatched = jwt.Audiences.Any(aud => string.Equals(aud, app.Slug, StringComparison.Ordinal));
        if (!audienceMatched)
            return (false, "OIDC token audience mismatch.");

        var azp = jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
        if (!string.IsNullOrWhiteSpace(azp) && !string.Equals(azp, app.Slug, StringComparison.Ordinal))
            return (false, "OIDC token authorized party mismatch.");

        return (true, null);
    }

    public bool ValidateToken(string token, out Guid sessionId, out string? tokenUse, out int? epoch)
    {
        sessionId = Guid.Empty;
        tokenUse = null;
        epoch = null;

        try
        {
            var parts = token.Split('.');

            switch (parts.Length)
            {
                case 3:
                    {
                        var (isValid, jwtResult) = authJwt.ValidateJwt(token);
                        if (!isValid) return false;
                        var jti = jwtResult?.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
                        tokenUse = jwtResult?.Claims.FirstOrDefault(c => c.Type == AuthJwtService.ClaimType)?.Value
                                   ?? jwtResult?.Claims.FirstOrDefault(c => c.Type == AuthJwtService.LegacyClaimTokenUse)?.Value
                                   ?? "user";
                        var epochClaim = jwtResult?.Claims.FirstOrDefault(c => c.Type == "epoch")?.Value;
                        if (int.TryParse(epochClaim, out var parsedEpoch))
                            epoch = parsedEpoch;
                        if (jti is null) return false;
                        return Guid.TryParse(jti, out sessionId);
                    }
                case 2:
                    {
                        var acceptUntil = config["Auth:LegacyTokens:AcceptUntil"];
                        if (DateTime.TryParse(acceptUntil, out var cutoff) && DateTime.UtcNow > cutoff.ToUniversalTime())
                            return false;
                        if (string.IsNullOrWhiteSpace(acceptUntil) && DateTime.UtcNow > LegacyDefaultCutoffUtc)
                            return false;
                        tokenUse = "user";
                        epoch = 0; // Legacy tokens use epoch 0
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

    private async Task HydratePerkAsync(SnAccount account)
    {
        try
        {
            var subscription = await subscriptions.GetPerkSubscription(account.Id);
            if (subscription is null)
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
                return;
            }

            var perk = SnWalletSubscription.FromProtoValue(subscription).ToReference();
            account.PerkSubscription = perk;
            account.PerkLevel = perk.PerkLevel;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to hydrate perk for account {AccountId}", account.Id);
            account.PerkSubscription = null;
            account.PerkLevel = 0;
        }
    }
}
