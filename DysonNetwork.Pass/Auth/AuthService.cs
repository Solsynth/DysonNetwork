using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Auth;

public class AuthService(
    AppDatabase db,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ICacheService cache,
    GeoService geo
)
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext!;
    public const string AuthCachePrefix = "auth:";

    /// <summary>
    /// Detect the risk of the current request to login
    /// and returns the required steps to login.
    /// </summary>
    /// <param name="request">The request context</param>
    /// <param name="account">The account to login</param>
    /// <returns>The required steps to login</returns>
    public async Task<int> DetectChallengeRisk(HttpRequest request, SnAccount account)
    {
        // 1) Find out how many authentication factors the account has enabled.
        var enabledFactors = await db.AccountAuthFactors
            .Where(f => f.AccountId == account.Id && f.Type != AccountAuthFactorType.PinCode)
            .Where(f => f.EnabledAt != null)
            .ToListAsync();
        var maxSteps = enabledFactors.Count;

        // We'll accumulate a "risk score" based on various factors.
        // Then we can decide how many total steps are required for the challenge.
        var riskScore = 0.0;

        // 2) Get login context from recent sessions
        var recentSessions = await db.AuthSessions
            .Where(s => s.AccountId == account.Id)
            .Where(s => s.LastGrantedAt != null)
            .OrderByDescending(s => s.LastGrantedAt)
            .Take(10)
            .ToListAsync();

        var recentChallengeIds =
            recentSessions
                .Where(s => s.ChallengeId != null)
                .Select(s => s.ChallengeId!.Value).ToList();
        var recentChallenges = await db.AuthChallenges.Where(c => recentChallengeIds.Contains(c.Id)).ToListAsync();

        var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = request.Headers.UserAgent.ToString();

        // 3) IP Address Risk Assessment
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            riskScore += 10;
        }
        else
        {
            // Check if IP has been used before
            var ipPreviouslyUsed = recentChallenges.Any(c => c.IpAddress == ipAddress);
            if (!ipPreviouslyUsed)
            {
                riskScore += 8;
            }

            // Check geographical distance for last known location
            var lastKnownIp = recentChallenges.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.IpAddress))?.IpAddress;
            if (!string.IsNullOrWhiteSpace(lastKnownIp) && lastKnownIp != ipAddress)
            {
                riskScore += 6;
            }
        }

        // 4) User Agent Analysis
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            riskScore += 5;
        }
        else
        {
            var uaPreviouslyUsed = recentChallenges.Any(c =>
                !string.IsNullOrWhiteSpace(c.UserAgent) &&
                string.Equals(c.UserAgent, userAgent, StringComparison.OrdinalIgnoreCase));

            if (!uaPreviouslyUsed)
            {
                riskScore += 4;

                // Check for suspicious user agent patterns
                if (userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
                    userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase) ||
                    userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase))
                {
                    riskScore += 8;
                }
            }
        }

        // 5) Time-based Risk Assessment
        var now = SystemClock.Instance.GetCurrentInstant();
        var lastLogin = recentSessions.FirstOrDefault()?.LastGrantedAt;

        if (lastLogin.HasValue)
        {
            var hoursSinceLastLogin = (now - lastLogin.Value).TotalHours;

            // Very high risk: long time since last activity
            if (hoursSinceLastLogin > 720) // 30 days
            {
                riskScore += 9;
            }
            else if (hoursSinceLastLogin > 168) // 7 days
            {
                riskScore += 6;
            }
            else if (hoursSinceLastLogin > 24) // 24 hours
            {
                riskScore += 3;
            }
        }
        else
        {
            // First login ever for this account
            riskScore += 7;
        }

        // 6) Failed Login Attempts Assessment
        var recentFailedChallenges = await db.AuthChallenges
            .Where(c => c.AccountId == account.Id)
            .Where(c => c.CreatedAt > now.Minus(Duration.FromHours(1))) // Last hour
            .Where(c => c.FailedAttempts > 0)
            .SumAsync(c => c.FailedAttempts);

        if (recentFailedChallenges > 0)
        {
            riskScore += Math.Min(recentFailedChallenges * 2, 10);
        }

        // 7) Account Security Status
        var totalAuthFactors = enabledFactors.Count;
        var timedCodeEnabled = enabledFactors.Any(f => f.Type == AccountAuthFactorType.TimedCode); // TOTP-like
        var pinCodeEnabled = enabledFactors.Any(f => f.Type == AccountAuthFactorType.PinCode);

        // Bonus for strong security
        if (totalAuthFactors >= 2)
            riskScore -= 3;
        else if (totalAuthFactors == 1)
            riskScore -= 1;

        // Additional bonuses for specific factors
        if (timedCodeEnabled) riskScore -= 2; // TOTP-like security
        if (pinCodeEnabled) riskScore -= 1;

        // 8) Device Trust Assessment
        var trustedDeviceIds = recentSessions
            .Where(s => s.CreatedAt > now.Minus(Duration.FromDays(30))) // Trust devices from last 30 days
            .Select(s => s.ClientId)
            .Where(id => id.HasValue)
            .Distinct()
            .ToList();

        // If using a trusted device, reduce risk (this will be validated later)
        if (trustedDeviceIds.Any())
        {
            riskScore -= 1;
        }

        // Clamp risk score between 0 and 20
        riskScore = Math.Max(0, Math.Min(riskScore, 20));

        // 9) Calculate required steps based on risk
        var riskWeight = maxSteps > 0 ? riskScore / 20.0 : 0.5; // Default 50% if no factors
        var totalRequiredSteps = (int)Math.Round(maxSteps * riskWeight);

        // Minimum 1 step, maximum all enabled factors
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
        var ipAddr = HttpContext.Connection.RemoteIpAddress?.ToString();
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
        if (device is not null) return device;
        device = new SnAuthClient
        {
            Platform = platform,
            DeviceId = deviceId,
            AccountId = accountId
        };
        if (deviceName is not null) device.DeviceName = deviceName;
        db.AuthClients.Add(device);
        await db.SaveChangesAsync();

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
                var content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
                    content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);

                return result?.Success == true;
            case "google":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
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

    /// <summary>
    /// Immediately revoke a session by setting expiry to now and clearing from cache
    /// This provides immediate invalidation of tokens and sessions, including all child sessions recursively.
    /// </summary>
    /// <param name="sessionId">Session ID to revoke</param>
    /// <returns>True if session was found and revoked, false otherwise</returns>
    public async Task<bool> RevokeSessionAsync(Guid sessionId)
    {
        var sessionsToRevokeIds = new HashSet<Guid>();
        await CollectSessionsToRevoke(sessionId, sessionsToRevokeIds);

        if (sessionsToRevokeIds.Count == 0)
        {
            return false;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var accountIdsToClearCache = new HashSet<Guid>();

        // Fetch all sessions to be revoked in one go
        var sessions = await db.AuthSessions
            .Where(s => sessionsToRevokeIds.Contains(s.Id))
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.ExpiredAt = now;
            accountIdsToClearCache.Add(session.AccountId);

            // Clear from cache immediately for each session
            await cache.RemoveAsync($"{AuthCachePrefix}{session.Id}");
        }

        db.AuthSessions.UpdateRange(sessions);
        await db.SaveChangesAsync();

        // Clear account-level cache groups
        foreach (var accountId in accountIdsToClearCache)
        {
            await cache.RemoveAsync($"{AuthCachePrefix}{accountId}");
        }

        return true;
    }

    /// <summary>
    /// Recursively collects all session IDs that need to be revoked, starting from a given session.
    /// </summary>
    /// <param name="currentSessionId">The session ID to start collecting from.</param>
    /// <param name="sessionsToRevoke">A HashSet to store the IDs of all sessions to be revoked.</param>
    private async Task CollectSessionsToRevoke(Guid currentSessionId, HashSet<Guid> sessionsToRevoke)
    {
        if (!sessionsToRevoke.Add(currentSessionId))
            return; // Already processed this session

        // Find direct children
        var childSessions = await db.AuthSessions
            .Where(s => s.ParentSessionId == currentSessionId)
            .Select(s => s.Id)
            .ToListAsync();

        foreach (var childId in childSessions)
        {
            await CollectSessionsToRevoke(childId, sessionsToRevoke);
        }
    }

    /// <summary>
    /// Revoke all sessions for an account (logout everywhere)
    /// </summary>
    /// <param name="accountId">Account ID to revoke all sessions for</param>
    /// <returns>Number of sessions revoked</returns>
    public async Task<int> RevokeAllSessionsForAccountAsync(Guid accountId)
    {
        var sessions = await db.AuthSessions
            .Where(s => s.AccountId == accountId && !s.ExpiredAt.HasValue)
            .ToListAsync();

        if (!sessions.Any())
        {
            return 0;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var session in sessions)
        {
            session.ExpiredAt = now;

            // Clear individual session cache
            var cacheKey = $"{AuthCachePrefix}{session.Id}";
            await cache.RemoveAsync(cacheKey);
        }

        // Clear account-level cache
        await cache.RemoveAsync($"{AuthCachePrefix}{accountId}");

        db.AuthSessions.UpdateRange(sessions);
        await db.SaveChangesAsync();

        return sessions.Count;
    }

    public string CreateToken(SnAuthSession session)
    {
        // Load the private key for signing
        var privateKeyPem = File.ReadAllText(config["AuthToken:PrivateKeyPath"]!);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        // Create and return a single token
        return CreateCompactToken(session.Id, rsa);
    }

    /// <summary>
    /// Create a session for a completed challenge, persist it, issue a token, and set the auth cookie.
    /// Keeps behavior identical to previous controller implementation.
    /// </summary>
    /// <param name="challenge">Completed challenge</param>
    /// <returns>Signed compact token</returns>
    /// <exception cref="ArgumentException">If challenge not completed or session already exists</exception>
    public async Task<string> CreateSessionAndIssueToken(SnAuthChallenge challenge)
    {
        if (challenge.StepRemain != 0)
            throw new ArgumentException("Challenge not yet completed.");

        var device = await GetOrCreateDeviceAsync(
            challenge.AccountId,
            challenge.DeviceId,
            challenge.DeviceName,
            challenge.Platform
        );

        var now = SystemClock.Instance.GetCurrentInstant();
        var session = new SnAuthSession
        {
            LastGrantedAt = now,
            ExpiredAt = now.Plus(Duration.FromDays(7)),
            AccountId = challenge.AccountId,
            IpAddress = challenge.IpAddress,
            UserAgent = challenge.UserAgent,
            Location = challenge.Location,
            Scopes = challenge.Scopes,
            Audiences = challenge.Audiences,
            ChallengeId = challenge.Id,
            ClientId = device.Id,
        };

        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        var tk = CreateToken(session);

        // Set cookie using HttpContext
        var cookieDomain = config["AuthToken:CookieDomain"]!;
        HttpContext.Response.Cookies.Append(AuthConstants.CookieTokenName, tk, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = cookieDomain,
            // Effectively never expire client-side (20 years)
            Expires = DateTime.UtcNow.AddYears(20)
        });

        return tk;
    }

    private static string CreateCompactToken(Guid sessionId, RSA rsa)
    {
        // Create the payload: just the session ID
        var payloadBytes = sessionId.ToByteArray();

        // Base64Url encode the payload
        var payloadBase64 = Base64UrlEncode(payloadBytes);

        // Sign the payload with RSA-SHA256
        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Base64Url encode the signature
        var signatureBase64 = Base64UrlEncode(signature);

        // Combine payload and signature with a dot
        return $"{payloadBase64}.{signatureBase64}";
    }

    public async Task<bool> ValidateSudoMode(SnAuthSession session, string? pinCode)
    {
        // Check if the session is already in sudo mode (cached)
        var sudoModeKey = $"accounts:{session.Id}:sudo";
        var (found, _) = await cache.GetAsyncWithStatus<bool>(sudoModeKey);

        if (found)
        {
            // Session is already in sudo mode
            return true;
        }

        // Check if the user has a pin code
        var hasPinCode = await db.AccountAuthFactors
            .Where(f => f.AccountId == session.AccountId)
            .Where(f => f.EnabledAt != null)
            .Where(f => f.Type == AccountAuthFactorType.PinCode)
            .AnyAsync();

        if (!hasPinCode)
        {
            // User doesn't have a pin code, no validation needed
            return true;
        }

        // If pin code is not provided, we can't validate
        if (string.IsNullOrEmpty(pinCode))
        {
            return false;
        }

        try
        {
            // Validate the pin code
            var isValid = await ValidatePinCode(session.AccountId, pinCode);

            if (isValid)
            {
                // Set session in sudo mode for 5 minutes
                await cache.SetAsync(sudoModeKey, true, TimeSpan.FromMinutes(5));
            }

            return isValid;
        }
        catch (InvalidOperationException)
        {
            // No pin code enabled for this account, so validation is successful
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

    public async Task<SnApiKey?> GetApiKey(Guid id, Guid? accountId = null)
    {
        var key = await db.ApiKeys
            .Include(e => e.Session)
            .Where(e => e.Id == id)
            .If(accountId.HasValue, q => q.Where(e => e.AccountId == accountId!.Value))
            .FirstOrDefaultAsync();
        return key;
    }

    public async Task<SnApiKey> CreateApiKey(Guid accountId, string label, Instant? expiredAt = null,
        SnAuthSession? parentSession = null)
    {
        var key = new SnApiKey
        {
            AccountId = accountId,
            Label = label,
            Session = new SnAuthSession
            {
                AccountId = accountId,
                ExpiredAt = expiredAt,
                ParentSessionId = parentSession?.Id
            },
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        return key;
    }

    public async Task<string> IssueApiKeyToken(SnApiKey key)
    {
        key.Session.LastGrantedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(key.Session);
        await db.SaveChangesAsync();
        var tk = CreateToken(key.Session);
        return tk;
    }

    public async Task RevokeApiKeyToken(SnApiKey key)
    {
        db.Remove(key);
        db.Remove(key.Session);
        await db.SaveChangesAsync();
    }

    public async Task<SnApiKey> RotateApiKeyToken(SnApiKey key)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var oldSessionId = key.SessionId;

            // Immediately expire old session and clear from cache
            if (oldSessionId != Guid.Empty)
            {
                var oldSession = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == oldSessionId);
                if (oldSession != null)
                {
                    oldSession.ExpiredAt = SystemClock.Instance.GetCurrentInstant();
                    db.AuthSessions.Update(oldSession);

                    // Clear old session from cache
                    var oldCacheKey = $"{AuthCachePrefix}{oldSessionId}";
                    await cache.RemoveAsync(oldCacheKey);
                }
            }

            // Create new session
            var newSession = new SnAuthSession
            {
                AccountId = key.AccountId,
                ExpiredAt = key.Session?.ExpiredAt
            };

            db.AuthSessions.Add(newSession);
            await db.SaveChangesAsync();

            // Update ApiKey to point to new session
            key.SessionId = newSession.Id;
            key.Session = newSession;
            db.ApiKeys.Update(key);
            await db.SaveChangesAsync();

            // Delete expired old session
            if (oldSessionId != Guid.Empty)
            {
                await db.AuthSessions.Where(s => s.Id == oldSessionId).ExecuteDeleteAsync<SnAuthSession>();
            }

            // Clear account-level cache to ensure new API key is picked up
            await cache.RemoveAsync($"{AuthCachePrefix}{key.AccountId}");

            await transaction.CommitAsync();
            return key;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // Helper methods for Base64Url encoding/decoding
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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

    /// <summary>
    /// Creates a new session derived from an existing parent session.
    /// </summary>
    /// <param name="parentSession">The existing session from which the new session is derived.</param>
    /// <param name="deviceId">The ID of the device for the new session.</param>
    /// <param name="deviceName">The name of the device for the new session.</param>
    /// <param name="platform">The platform of the device for the new session.</param>
    /// <param name="expiredAt">Optional: The expiration time for the new session.</param>
    /// <returns>The newly created SnAuthSession.</returns>
    public async Task<SnAuthSession> CreateSessionFromParentAsync(
        SnAuthSession parentSession,
        string deviceId,
        string? deviceName,
        ClientPlatform platform,
        Instant? expiredAt = null
    )
    {
        var device = await GetOrCreateDeviceAsync(parentSession.AccountId, deviceId, deviceName, platform);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var geoLocation = ipAddress is not null ? geo.GetPointFromIp(ipAddress) : null;

        var now = SystemClock.Instance.GetCurrentInstant();
        var session = new SnAuthSession
        {
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = geoLocation,
            AccountId = parentSession.AccountId,
            CreatedAt = now,
            LastGrantedAt = now,
            ExpiredAt = expiredAt,
            ParentSessionId = parentSession.Id,
            ClientId = device.Id,
        };

        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }
}