using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Padlock.Account;
using NodaTime;
using Npgsql;

namespace DysonNetwork.Padlock.Auth;

public class CaptchaVerificationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public class AuthService(
    AppDatabase db,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ICacheService cache,
    GeoService geo,
    RemoteSubscriptionService subscriptions,
    AuthJwtService authJwt,
    ILogger<AuthService> logger,
    ActionLogService actionLogs
)
{
    public sealed record TokenPair(
        string AccessToken,
        string RefreshToken,
        Instant AccessTokenExpiresAt,
        Instant RefreshTokenExpiresAt
    );

    private HttpContext HttpContext => httpContextAccessor.HttpContext!;
    public const string AuthCachePrefix = "auth:";
    private const string AccountVersionPrefix = "auth:account_ver:";

    public async Task<int> DetectChallengeRisk(HttpRequest request, SnAccount account)
    {
        var enabledFactors = await db.AccountAuthFactors
            .Where(f => f.AccountId == account.Id)
            .Where(f => f.Type != AccountAuthFactorType.PinCode && f.Type != AccountAuthFactorType.RecoveryCode)
            .Where(f => f.EnabledAt != null)
            .ToListAsync();
        var maxSteps = enabledFactors.Count;
        if (maxSteps == 0)
            throw new ArgumentException("Account has no authentication factors configured.");

        var riskScore = 0.0;
        var recentSessions = await db.AuthSessions
            .Where(s => s.AccountId == account.Id)
            .Where(s => s.LastGrantedAt != null)
            .OrderByDescending(s => s.LastGrantedAt)
            .Take(10)
            .ToListAsync();

        var recentChallengeIds = recentSessions
            .Where(s => s.ChallengeId != null)
            .Select(s => s.ChallengeId!.Value).ToList();
        var recentChallenges = await db.AuthChallenges.Where(c => recentChallengeIds.Contains(c.Id)).ToListAsync();

        var ipAddress = request.HttpContext.GetClientIpAddress();
        var userAgent = request.Headers.UserAgent.ToString();

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            riskScore += 10;
        }
        else
        {
            var ipPreviouslyUsed = recentChallenges.Any(c => c.IpAddress == ipAddress);
            if (!ipPreviouslyUsed) riskScore += 8;
            var lastKnownIp = recentChallenges.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.IpAddress))?.IpAddress;
            if (!string.IsNullOrWhiteSpace(lastKnownIp) && lastKnownIp != ipAddress) riskScore += 6;
        }

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            riskScore += 5;
        }
        else
        {
            var uaPreviouslyUsed = recentChallenges.Any(c =>
                !string.IsNullOrWhiteSpace(c.UserAgent) &&
                string.Equals(c.UserAgent, userAgent, StringComparison.OrdinalIgnoreCase));
            if (!uaPreviouslyUsed) riskScore += 4;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var lastLogin = recentSessions.FirstOrDefault()?.LastGrantedAt;
        if (lastLogin.HasValue)
        {
            var hoursSinceLastLogin = (now - lastLogin.Value).TotalHours;
            if (hoursSinceLastLogin > 720) riskScore += 9;
            else if (hoursSinceLastLogin > 168) riskScore += 6;
            else if (hoursSinceLastLogin > 24) riskScore += 3;
        }
        else
        {
            riskScore += 7;
        }

        var recentFailedChallenges = await db.AuthChallenges
            .Where(c => c.AccountId == account.Id)
            .Where(c => c.CreatedAt > now.Minus(Duration.FromHours(1)))
            .Where(c => c.FailedAttempts > 0)
            .SumAsync(c => c.FailedAttempts);
        if (recentFailedChallenges > 0) riskScore += Math.Min(recentFailedChallenges * 2, 10);

        var totalAuthFactors = enabledFactors.Count;
        var timedCodeEnabled = enabledFactors.Any(f => f.Type == AccountAuthFactorType.TimedCode);
        var pinCodeEnabled = enabledFactors.Any(f => f.Type == AccountAuthFactorType.PinCode);

        if (totalAuthFactors >= 2) riskScore -= 3;
        else if (totalAuthFactors == 1) riskScore -= 1;
        if (timedCodeEnabled) riskScore -= 2;
        if (pinCodeEnabled) riskScore -= 1;

        var trustedDeviceIds = recentSessions
            .Where(s => s.CreatedAt > now.Minus(Duration.FromDays(30)))
            .Select(s => s.ClientId)
            .Where(id => id.HasValue)
            .Distinct()
            .ToList();
        if (trustedDeviceIds.Any()) riskScore -= 1;

        riskScore = Math.Max(0, Math.Min(riskScore, 20));
        var riskWeight = maxSteps > 0 ? riskScore / 20.0 : 0.5;
        var totalRequiredSteps = (int)Math.Round(maxSteps * riskWeight);
        totalRequiredSteps = Math.Max(Math.Min(totalRequiredSteps, maxSteps), 1);

        return totalRequiredSteps;
    }

    public async Task<SnAuthSession> CreateSessionForOidcAsync(
        SnAccount account,
        Instant time,
        Guid? customAppId = null,
        SnAuthSession? parentSession = null
    )
    {
        var ipAddr = HttpContext.GetClientIpAddress();
        var geoLocation = ipAddr is not null ? geo.GetPointFromIp(ipAddr) : null;
        var session = new SnAuthSession
        {
            AccountId = account.Id,
            CreatedAt = time,
            LastGrantedAt = time,
            IpAddress = ipAddr,
            UserAgent = HttpContext.Request.Headers.UserAgent,
            Location = geoLocation,
            AppId = customAppId,
            ParentSessionId = parentSession?.Id,
            Type = customAppId is not null ? SessionType.OAuth : SessionType.Oidc,
        };

        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();
        await actionLogs.CreateActionLogAsync(
            account.Id,
            ActionLogType.NewLogin,
            new Dictionary<string, object>
            {
                ["session_type"] = session.Type.ToString(),
                ["app_id"] = customAppId?.ToString() ?? string.Empty
            },
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.GetClientIpAddress(),
            null,
            session.Id
        );

        return session;
    }

    public async Task<SnAuthClient> GetOrCreateDeviceAsync(
        Guid accountId,
        string deviceId,
        string? deviceName = null,
        ClientPlatform platform = ClientPlatform.Unidentified
    )
    {
        var device = await db.AuthClients
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.AccountId == accountId);
        if (device is not null)
        {
            if (device.DeletedAt is not null)
            {
                device.DeletedAt = null;
                device.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
                if (deviceName is not null) device.DeviceName = deviceName;
                await db.SaveChangesAsync();
            }
            return device;
        }

        device = await db.AuthClients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.AccountId == accountId);
        if (device is not null)
        {
            device.DeletedAt = null;
            device.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
            if (deviceName is not null) device.DeviceName = deviceName;
            await db.SaveChangesAsync();
            return device;
        }

        device = new SnAuthClient
        {
            Platform = platform,
            DeviceId = deviceId,
            AccountId = accountId
        };
        if (deviceName is not null) device.DeviceName = deviceName;
        db.AuthClients.Add(device);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(device).State = EntityState.Detached;
            device = await db.AuthClients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.AccountId == accountId);
            if (device is null)
            {
                throw;
            }
            if (device.DeletedAt is not null)
            {
                device.DeletedAt = null;
                device.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
                await db.SaveChangesAsync();
            }
        }

        return device;
    }

    public async Task<bool> ValidateCaptcha(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var provider = config.GetSection("Captcha")["Provider"]?.ToLower();
        var apiSecret = config.GetSection("Captcha")["ApiSecret"];

        var client = httpClientFactory.CreateClient();
        var jsonOpts = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        switch (provider)
        {
            case "cloudflare":
                var content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);
                return result?.Success == true;
            case "google":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                response = await client.PostAsync("https://www.google.com/recaptcha/siteverify", content);
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync();
                result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);
                return result?.Success == true;
            case "hcaptcha":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                response = await client.PostAsync("https://hcaptcha.com/siteverify", content);
                response.EnsureSuccessStatusCode();

                json = await response.Content.ReadAsStringAsync();
                result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);

                return result?.Success == true;
            default:
                throw new ArgumentException("The server misconfigured for the captcha.");
        }
    }

    public async Task<bool> RevokeSessionAsync(Guid sessionId)
    {
        var sessionsToRevokeIds = await CollectSessionsToRevokeAsync(sessionId);
        if (sessionsToRevokeIds.Count == 0) return false;

        var now = SystemClock.Instance.GetCurrentInstant();
        var sessions = await db.AuthSessions
            .Where(s => sessionsToRevokeIds.Contains(s.Id))
            .Select(s => new { s.Id, s.AccountId })
            .ToListAsync();
        if (sessions.Count == 0) return false;

        // Increment epoch and set expired_at for all sessions to revoke
        await db.AuthSessions
            .Where(s => sessionsToRevokeIds.Contains(s.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ExpiredAt, now)
                .SetProperty(x => x.Epoch, x => x.Epoch + 1));

        foreach (var sessionIdToClear in sessions.Select(s => s.Id))
        {
            // Invalidate AuthScheme's session cache (epoch increment invalidates tokens)
            await cache.RemoveAsync(AuthCacheConstants.Session(sessionIdToClear.ToString()));
        }
        foreach (var accountId in sessions.Select(s => s.AccountId).Distinct())
        {
            await BumpAccountVersion(accountId);
        }

        return true;
    }

    private async Task<HashSet<Guid>> CollectSessionsToRevokeAsync(Guid rootSessionId)
    {
        var collected = new HashSet<Guid>();
        var frontier = new List<Guid> { rootSessionId };

        while (frontier.Count > 0)
        {
            var idsInBatch = frontier.Where(collected.Add).ToList();
            if (idsInBatch.Count == 0) break;
            frontier = await db.AuthSessions
                .Where(s => s.ParentSessionId.HasValue && idsInBatch.Contains(s.ParentSessionId.Value))
                .Select(s => s.Id)
                .ToListAsync();
        }

        return collected;
    }

    public async Task<int> RevokeAllSessionsForAccountAsync(Guid accountId)
    {
        var sessions = await db.AuthSessions
            .Where(s => s.AccountId == accountId && !s.ExpiredAt.HasValue)
            .Select(s => s.Id)
            .ToListAsync();
        if (sessions.Count == 0) return 0;

        var now = SystemClock.Instance.GetCurrentInstant();
        await db.AuthSessions
            .Where(s => sessions.Contains(s.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ExpiredAt, now)
                .SetProperty(x => x.Epoch, x => x.Epoch + 1));

        foreach (var sessionIdToClear in sessions)
        {
            // Invalidate AuthScheme's session cache (epoch increment invalidates tokens)
            await cache.RemoveAsync(AuthCacheConstants.Session(sessionIdToClear.ToString()));
        }
        await BumpAccountVersion(accountId);

        return sessions.Count;
    }

    private Duration GetAccessTokenLifetime()
    {
        var cfg = config["AuthToken:AccessTokenLifetime"];
        if (TimeSpan.TryParse(cfg, out var parsed) && parsed > TimeSpan.Zero)
            return Duration.FromTimeSpan(parsed);
        return Duration.FromHours(1);
    }

    private Duration GetRefreshTokenLifetime()
    {
        var cfg = config["AuthToken:RefreshTokenLifetime"];
        if (TimeSpan.TryParse(cfg, out var parsed) && parsed > TimeSpan.Zero)
            return Duration.FromTimeSpan(parsed);
        return Duration.FromDays(30);
    }

    private Instant ResolveAccessExpiry(SnAuthSession session, Instant now)
    {
        var target = now.Plus(GetAccessTokenLifetime());
        if (session.ExpiredAt.HasValue && session.ExpiredAt.Value < target)
            return session.ExpiredAt.Value;
        return target;
    }

    public async Task<string> CreateToken(SnAuthSession session)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == session.AccountId)
                      ?? throw new InvalidOperationException("Session account not found.");
        await HydratePerkAsync(account);
        var version = await GetAccountVersion(session.AccountId);
        var now = SystemClock.Instance.GetCurrentInstant();
        return authJwt.CreateUserToken(session, account, version, ResolveAccessExpiry(session, now));
    }

    public async Task<TokenPair> CreateTokenPair(SnAuthSession session)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == session.AccountId)
                      ?? throw new InvalidOperationException("Session account not found.");
        await HydratePerkAsync(account);
        var version = await GetAccountVersion(session.AccountId);
        var now = SystemClock.Instance.GetCurrentInstant();
        var accessExpiresAt = ResolveAccessExpiry(session, now);
        var refreshExpiresAt = session.ExpiredAt ?? now.Plus(GetRefreshTokenLifetime());
        var accessToken = authJwt.CreateUserToken(session, account, version, accessExpiresAt);
        var refreshToken = authJwt.CreateRefreshToken(session, version, refreshExpiresAt);
        return new TokenPair(accessToken, refreshToken, accessExpiresAt, refreshExpiresAt);
    }

    public async Task<TokenPair> CreateSessionAndIssueTokens(SnAuthChallenge challenge)
    {
        if (challenge.StepTotal <= 0)
            throw new ArgumentException("Challenge has no authentication factors configured.");
        if (challenge.StepRemain != 0)
            throw new ArgumentException("Challenge not yet completed.");
        var now = SystemClock.Instance.GetCurrentInstant();
        if (challenge.ExpiredAt.HasValue && challenge.ExpiredAt < now)
            throw new ArgumentException("Challenge has expired.");

        var existingSession = await db.AuthSessions
            .Where(s => s.ChallengeId == challenge.Id && s.AccountId == challenge.AccountId)
            .FirstOrDefaultAsync();
        if (existingSession is not null)
        {
            existingSession.LastGrantedAt = now;
            db.Update(existingSession);
            await db.SaveChangesAsync();
            return await CreateTokenPair(existingSession);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var device = await GetOrCreateDeviceAsync(
                challenge.AccountId,
                challenge.DeviceId,
                challenge.DeviceName,
                challenge.Platform
            );

            var session = new SnAuthSession
            {
                Type = SessionType.Login,
                LastGrantedAt = now,
                ExpiredAt = now.Plus(GetRefreshTokenLifetime()),
                AccountId = challenge.AccountId,
                IpAddress = challenge.IpAddress,
                UserAgent = challenge.UserAgent,
                Location = challenge.Location,
                Scopes = challenge.Scopes,
                Audiences = challenge.Audiences,
                ChallengeId = challenge.Id,
                ClientId = device.Id,
            };

            challenge.ExpiredAt = now;
            db.AuthSessions.Add(session);
            db.AuthChallenges.Update(challenge);
            await db.SaveChangesAsync();

            var pair = await CreateTokenPair(session);
            await actionLogs.CreateActionLogAsync(
                challenge.AccountId,
                ActionLogType.NewLogin,
                new Dictionary<string, object>
                {
                    ["session_type"] = session.Type.ToString(),
                    ["challenge_id"] = challenge.Id.ToString()
                },
                challenge.UserAgent,
                challenge.IpAddress,
                challenge.Location is null ? null : JsonSerializer.Serialize(challenge.Location),
                session.Id
            );

            await tx.CommitAsync();
            return pair;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogWarning(ex, "CreateSessionAndIssueToken failed for challenge {ChallengeId}", challenge.Id);
            throw;
        }
    }

    public async Task<TokenPair> RefreshSessionAndIssueTokens(string refreshToken)
    {
        var (isValid, jwt) = authJwt.ValidateJwt(refreshToken);
        if (!isValid || jwt is null)
            throw new ArgumentException("Invalid refresh token.");

        var tokenUse = jwt.Claims.FirstOrDefault(c => c.Type == AuthJwtService.ClaimType)?.Value
                       ?? jwt.Claims.FirstOrDefault(c => c.Type == AuthJwtService.LegacyClaimTokenUse)?.Value;
        if (!string.Equals(tokenUse, "refresh", StringComparison.Ordinal))
            throw new ArgumentException("Invalid refresh token.");

        var jti = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("Invalid refresh token.");

        var sessionIdText = jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value ?? jti;
        if (!Guid.TryParse(sessionIdText, out var sessionId))
            throw new ArgumentException("Invalid refresh token.");

        var accountIdText = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (!Guid.TryParse(accountIdText, out var accountId))
            throw new ArgumentException("Invalid refresh token.");

        var tokenVer = jwt.Claims.FirstOrDefault(c => c.Type == "ver")?.Value;
        var currentVer = await GetAccountVersion(accountId);
        if (int.TryParse(tokenVer, out var claimVer) && claimVer < currentVer)
            throw new ArgumentException("Refresh token has been invalidated.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var session = await db.AuthSessions
            .Include(s => s.Account)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.AccountId == accountId);
        if (session is null)
            throw new ArgumentException("Session was not found.");
        if (session.ExpiredAt.HasValue && session.ExpiredAt.Value <= now)
            throw new ArgumentException("Session has been expired.");

        // Validate epoch (replace JTI revocation check)
        var tokenEpochText = jwt.Claims.FirstOrDefault(c => c.Type == "epoch")?.Value;
        if (int.TryParse(tokenEpochText, out var tokenEpoch) && tokenEpoch != session.Epoch)
            throw new ArgumentException("Refresh token has been revoked.");

        session.LastGrantedAt = now;
        session.ExpiredAt = now.Plus(GetRefreshTokenLifetime());
        session.Epoch++; // Increment epoch on refresh
        db.AuthSessions.Update(session);
        await db.SaveChangesAsync();

        // Invalidate AuthScheme's session cache since expiration changed
        await cache.RemoveAsync(AuthCacheConstants.Session(session.Id.ToString()));

        return await CreateTokenPair(session);
    }

    public async Task PopulatePerkAsync(SnAccount account)
    {
        await HydratePerkAsync(account);
    }

    public async Task TrackAuthenticatedActivityAsync(SnAuthSession session, string? ipAddress = null)
    {
        var activityCacheKey = $"auth:activity:{session.AccountId}";
        var (trackedRecently, _) = await cache.GetAsyncWithStatus<bool>(activityCacheKey);
        if (trackedRecently) return;

        var resolvedIpAddress = string.IsNullOrWhiteSpace(ipAddress) ? session.IpAddress : ipAddress;
        await actionLogs.CreateActionLogAsync(
            session.AccountId,
            ActionLogType.AccountActive,
            new Dictionary<string, object>
            {
                ["session_id"] = session.Id.ToString(),
                ["session_type"] = session.Type.ToString(),
                ["app_id"] = session.AppId?.ToString() ?? string.Empty
            },
            session.UserAgent,
            resolvedIpAddress,
            session.Location is null ? null : JsonSerializer.Serialize(session.Location),
            session.Id
        );

        await cache.SetAsync(activityCacheKey, true, TimeSpan.FromHours(1));
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

    public async Task<bool> ValidateSudoMode(SnAuthSession session, string? pinCode)
    {
        var sudoModeKey = $"accounts:{session.Id}:sudo";
        var (found, _) = await cache.GetAsyncWithStatus<bool>(sudoModeKey);
        if (found) return true;

        var hasPinCode = await db.AccountAuthFactors
            .Where(f => f.AccountId == session.AccountId)
            .Where(f => f.EnabledAt != null)
            .Where(f => f.Type == AccountAuthFactorType.PinCode)
            .AnyAsync();

        if (!hasPinCode) return true;
        if (string.IsNullOrEmpty(pinCode)) return false;

        try
        {
            var isValid = await ValidatePinCode(session.AccountId, pinCode);
            if (isValid) await cache.SetAsync(sudoModeKey, true, TimeSpan.FromMinutes(5));
            return isValid;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public async Task<bool> ValidatePinCode(Guid accountId, string pinCode)
    {
        var factor = await db.AccountAuthFactors
            .Where(f => f.AccountId == accountId)
            .Where(f => f.EnabledAt != null)
            .Where(f => f.Type == AccountAuthFactorType.PinCode)
            .FirstOrDefaultAsync();
        if (factor is null) throw new InvalidOperationException("No pin code enabled for this account.");
        return factor.VerifyPassword(pinCode);
    }

    public async Task<TokenPair> RecoverAccountWithRecoveryCodeAsync(
        SnAccount account,
        string recoveryCode,
        string deviceId,
        ClientPlatform platform,
        string? deviceName = null
    )
    {
        var recoveryFactor = await db.AccountAuthFactors
            .Where(f => f.AccountId == account.Id)
            .Where(f => f.Type == AccountAuthFactorType.RecoveryCode)
            .Where(f => f.EnabledAt != null)
            .FirstOrDefaultAsync();

        if (recoveryFactor is null)
            throw new ArgumentException("Recovery code factor not found.");

        if (recoveryFactor.Secret != recoveryCode)
            throw new ArgumentException("Invalid recovery code.");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var factorsToDisable = await db.AccountAuthFactors
                .Where(f => f.AccountId == account.Id)
                .Where(f => f.Type != AccountAuthFactorType.Password)
                .Where(f => f.Type != AccountAuthFactorType.RecoveryCode)
                .Where(f => f.EnabledAt != null)
                .ToListAsync();

            foreach (var factor in factorsToDisable)
            {
                factor.EnabledAt = null;
                db.AccountAuthFactors.Update(factor);
            }

            recoveryFactor.EnabledAt = null;
            db.AccountAuthFactors.Update(recoveryFactor);

            var revokedCount = await RevokeAllSessionsForAccountAsync(account.Id);

            var now = SystemClock.Instance.GetCurrentInstant();
            var device = await GetOrCreateDeviceAsync(account.Id, deviceId, deviceName, platform);

            var ipAddress = HttpContext.GetClientIpAddress();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
            var geoLocation = ipAddress is not null ? geo.GetPointFromIp(ipAddress) : null;

            var session = new SnAuthSession
            {
                Type = SessionType.Login,
                CreatedAt = now,
                LastGrantedAt = now,
                ExpiredAt = now.Plus(GetRefreshTokenLifetime()),
                AccountId = account.Id,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Location = geoLocation,
                ClientId = device.Id,
            };

            db.AuthSessions.Add(session);
            await db.SaveChangesAsync();

            await actionLogs.CreateActionLogAsync(
                account.Id,
                ActionLogType.AccountRecovery,
                new Dictionary<string, object>
                {
                    ["factors_disabled"] = factorsToDisable
                        .Select(f => f.Type.ToString())
                        .Concat([AccountAuthFactorType.RecoveryCode.ToString()])
                        .ToList(),
                    ["sessions_revoked"] = revokedCount
                },
                userAgent,
                ipAddress,
                geoLocation is null ? null : JsonSerializer.Serialize(geoLocation),
                session.Id
            );

            await tx.CommitAsync();
            return await CreateTokenPair(session);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<SnApiKey?> GetApiKey(Guid id, Guid? accountId = null)
    {
        var key = await db.ApiKeys
            .Include(e => e.Session)
            .Where(e => e.Id == id)
            .If(accountId.HasValue, q => q.Where(e => e.AccountId == accountId!.Value))
            .FirstOrDefaultAsync();
        return key;
    }

    public async Task<SnApiKey> CreateApiKey(Guid accountId, string label, Instant? expiredAt = null, SnAuthSession? parentSession = null)
    {
        var normalizedLabel = label.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLabel))
            throw new ArgumentException("Label is required.", nameof(label));

        var now = SystemClock.Instance.GetCurrentInstant();
        if (expiredAt.HasValue && expiredAt <= now)
            throw new ArgumentException("ExpiredAt must be in the future.", nameof(expiredAt));

        var key = new SnApiKey
        {
            AccountId = accountId,
            AppId = parentSession?.AppId,
            Label = normalizedLabel,
            Session = new SnAuthSession
            {
                AccountId = accountId,
                Type = SessionType.ApiKey,
                AppId = parentSession?.AppId,
                ExpiredAt = expiredAt,
                LastGrantedAt = now,
                ParentSessionId = parentSession?.Id
            },
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();
        return key;
    }

    public async Task<string> IssueApiKeyToken(SnApiKey key)
    {
        var sessionId = key.SessionId != Guid.Empty ? key.SessionId : key.Session?.Id ?? Guid.Empty;
        if (sessionId == Guid.Empty)
            throw new InvalidOperationException("API key session is not available.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var updatedRows = await db.AuthSessions
            .Where(s => s.Id == sessionId)
            .Where(s => !s.ExpiredAt.HasValue || s.ExpiredAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastGrantedAt, now));
        if (updatedRows == 0)
            throw new InvalidOperationException("API key session has expired or does not exist.");

        var session = key.Session ?? await db.AuthSessions.FirstAsync(s => s.Id == sessionId);
        var accountVersion = await GetAccountVersion(key.AccountId);
        if (key.Session is not null) key.Session.LastGrantedAt = now;
        return authJwt.CreateBotToken(key, session, accountVersion);
    }

    public async Task RevokeApiKeyToken(SnApiKey key)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await using var transaction = await db.Database.BeginTransactionAsync();

        key.DeletedAt = now;
        db.ApiKeys.Update(key);
        await db.SaveChangesAsync();

        // Increment epoch to revoke existing tokens (RevokeSessionAsync handles this)
        await BumpAccountVersion(key.AccountId);
        await RevokeSessionAsync(key.SessionId);
        await transaction.CommitAsync();
    }

    public async Task<SnApiKey> RotateApiKeyToken(SnApiKey key)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var oldSession = await db.AuthSessions
                .Where(s => s.Id == key.SessionId && s.AccountId == key.AccountId)
                .FirstOrDefaultAsync();
            if (oldSession is null)
                throw new InvalidOperationException("API key session was not found.");

            var originalExpiry = oldSession.ExpiredAt;
            oldSession.ExpiredAt = now;
            oldSession.LastGrantedAt = now;
            oldSession.Epoch++; // Increment epoch to revoke old tokens
            db.AuthSessions.Update(oldSession);

            var newSession = new SnAuthSession
            {
                AccountId = key.AccountId,
                Type = oldSession.Type,
                IpAddress = oldSession.IpAddress,
                UserAgent = oldSession.UserAgent,
                Location = oldSession.Location,
                Audiences = oldSession.Audiences.ToList(),
                Scopes = oldSession.Scopes.ToList(),
                AppId = oldSession.AppId,
                ClientId = oldSession.ClientId,
                LastGrantedAt = now,
                ParentSessionId = oldSession.ParentSessionId,
                ExpiredAt = originalExpiry
            };

            db.AuthSessions.Add(newSession);
            key.SessionId = newSession.Id;
            key.Session = newSession;
            key.AppId = oldSession.AppId;
            db.ApiKeys.Update(key);
            await db.SaveChangesAsync();

            await BumpAccountVersion(key.AccountId);

            // Invalidate AuthScheme's cache for the old session
            await cache.RemoveAsync(AuthCacheConstants.Session(oldSession.Id.ToString()));

            await transaction.CommitAsync();
            return key;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<SnAuthorizedApp> UpsertAuthorizedAppAsync(
        Guid accountId,
        Guid appId,
        AuthorizedAppType type,
        string? appSlug = null,
        string? appName = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var existing = await db.AuthorizedApps
            .FirstOrDefaultAsync(x =>
                x.AccountId == accountId &&
                x.AppId == appId &&
                x.Type == type &&
                x.DeletedAt == null);

        if (existing is null)
        {
            var created = new SnAuthorizedApp
            {
                AccountId = accountId,
                AppId = appId,
                Type = type,
                AppSlug = appSlug,
                AppName = appName,
                LastAuthorizedAt = now,
                LastUsedAt = now
            };
            db.AuthorizedApps.Add(created);
            await db.SaveChangesAsync();
            return created;
        }

        existing.LastAuthorizedAt = now;
        existing.LastUsedAt = now;
        if (!string.IsNullOrWhiteSpace(appSlug)) existing.AppSlug = appSlug;
        if (!string.IsNullOrWhiteSpace(appName)) existing.AppName = appName;
        db.AuthorizedApps.Update(existing);
        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<int> RevokeAuthorizedAppAccessAsync(
        Guid accountId,
        Guid appId,
        AuthorizedAppType? type = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var appAuthsQuery = db.AuthorizedApps
            .Where(x => x.AccountId == accountId && x.AppId == appId && x.DeletedAt == null);
        if (type.HasValue)
            appAuthsQuery = appAuthsQuery.Where(x => x.Type == type.Value);

        var appAuths = await appAuthsQuery.ToListAsync();
        if (appAuths.Count == 0) return 0;

        foreach (var appAuth in appAuths)
        {
            appAuth.DeletedAt = now;
            appAuth.LastUsedAt = now;
        }
        db.AuthorizedApps.UpdateRange(appAuths);
        await db.SaveChangesAsync();

        var sessions = await db.AuthSessions
            .Where(s =>
                s.AccountId == accountId &&
                s.AppId == appId &&
                (!s.ExpiredAt.HasValue || s.ExpiredAt > now))
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var sessionId in sessions)
            await RevokeSessionAsync(sessionId);

        var apiKeys = await db.ApiKeys
            .Where(k =>
                k.AccountId == accountId &&
                k.AppId == appId &&
                k.DeletedAt == null)
            .Include(k => k.Session)
            .ToListAsync();

        foreach (var apiKey in apiKeys)
            await RevokeApiKeyToken(apiKey);

        await actionLogs.CreateActionLogAsync(
            accountId,
            ActionLogType.AuthorizedAppDeauthorize,
            new Dictionary<string, object>
            {
                ["app_id"] = appId,
                ["count"] = appAuths.Count,
                ["type"] = type?.ToString() ?? string.Empty
            },
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.GetClientIpAddress()
        );

        return appAuths.Count;
    }

    public async Task<SnAuthSession> CreateSessionFromParentAsync(
        SnAuthSession parentSession,
        string deviceId,
        string? deviceName,
        ClientPlatform platform,
        Instant? expiredAt = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var parent = await db.AuthSessions
            .AsNoTracking()
            .Where(s => s.Id == parentSession.Id && s.AccountId == parentSession.AccountId)
            .FirstOrDefaultAsync();
        if (parent is null) throw new InvalidOperationException("Parent session not found.");
        if (parent.ExpiredAt.HasValue && parent.ExpiredAt <= now)
            throw new InvalidOperationException("Parent session is expired.");

        var device = await GetOrCreateDeviceAsync(parentSession.AccountId, deviceId, deviceName, platform);

        var ipAddress = HttpContext.GetClientIpAddress();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var geoLocation = ipAddress is not null ? geo.GetPointFromIp(ipAddress) : null;

        var finalExpiredAt = expiredAt ?? parent.ExpiredAt;
        if (finalExpiredAt.HasValue && finalExpiredAt <= now)
            throw new InvalidOperationException("Requested expiration time is already in the past.");

        var session = new SnAuthSession
        {
            Type = parent.Type,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = geoLocation,
            AccountId = parentSession.AccountId,
            CreatedAt = now,
            LastGrantedAt = now,
            ExpiredAt = finalExpiredAt,
            ParentSessionId = parent.Id,
            ClientId = device.Id,
            Audiences = parent.Audiences.ToList(),
            Scopes = parent.Scopes.ToList(),
            AppId = parent.AppId,
        };

        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private async Task<int> GetAccountVersion(Guid accountId)
    {
        var (found, value) = await cache.GetAsyncWithStatus<int>($"{AccountVersionPrefix}{accountId}");
        return found ? value : 0;
    }

    private async Task<int> BumpAccountVersion(Guid accountId)
    {
        var next = await GetAccountVersion(accountId) + 1;
        await cache.SetAsync($"{AccountVersionPrefix}{accountId}", next, TimeSpan.FromDays(90));
        return next;
    }
}
