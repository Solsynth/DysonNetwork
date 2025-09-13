using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Auth;

public class AuthService(
    AppDatabase db,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ICacheService cache,
    ILogger<AuthService> logger
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
    public async Task<int> DetectChallengeRisk(HttpRequest request, Account.Account account)
    {
        // 1) Find out how many authentication factors the account has enabled.
        var maxSteps = await db.AccountAuthFactors
            .Where(f => f.AccountId == account.Id)
            .Where(f => f.EnabledAt != null)
            .CountAsync();

        // We’ll accumulate a “risk score” based on various factors.
        // Then we can decide how many total steps are required for the challenge.
        var riskScore = 0;

        // 2) Get the remote IP address from the request (if any).
        var ipAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var lastActiveInfo = await db.AuthSessions
            .OrderByDescending(s => s.LastGrantedAt)
            .Include(s => s.Challenge)
            .Where(s => s.AccountId == account.Id)
            .FirstOrDefaultAsync();

        // Example check: if IP is missing or in an unusual range, increase the risk.
        // (This is just a placeholder; in reality, you’d integrate with GeoIpService or a custom check.)
        if (string.IsNullOrWhiteSpace(ipAddress))
            riskScore += 1;
        else
        {
            if (!string.IsNullOrEmpty(lastActiveInfo?.Challenge?.IpAddress) &&
                !lastActiveInfo.Challenge.IpAddress.Equals(ipAddress, StringComparison.OrdinalIgnoreCase))
                riskScore += 1;
        }

        // 3) (Optional) Check how recent the last login was.
        // If it was a long time ago, the risk might be higher.
        var now = SystemClock.Instance.GetCurrentInstant();
        var daysSinceLastActive = lastActiveInfo?.LastGrantedAt is not null
            ? (now - lastActiveInfo.LastGrantedAt.Value).TotalDays
            : double.MaxValue;
        if (daysSinceLastActive > 30)
            riskScore += 1;

        // 4) Combine base “maxSteps” (the number of enabled factors) with any accumulated risk score.
        const int totalRiskScore = 3;
        var totalRequiredSteps = (int)Math.Round((float)maxSteps * riskScore / totalRiskScore);
        // Clamp the steps
        totalRequiredSteps = Math.Max(Math.Min(totalRequiredSteps, maxSteps), 1);

        return totalRequiredSteps;
    }

    public async Task<AuthSession> CreateSessionForOidcAsync(Account.Account account, Instant time,
        Guid? customAppId = null)
    {
        var challenge = new AuthChallenge
        {
            AccountId = account.Id,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent,
            StepRemain = 1,
            StepTotal = 1,
            Type = customAppId is not null ? ChallengeType.OAuth : ChallengeType.Oidc
        };

        var session = new AuthSession
        {
            AccountId = account.Id,
            CreatedAt = time,
            LastGrantedAt = time,
            Challenge = challenge,
            AppId = customAppId
        };

        db.AuthChallenges.Add(challenge);
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    public async Task<AuthClient> GetOrCreateDeviceAsync(
        Guid accountId,
        string deviceId,
        string? deviceName = null,
        ClientPlatform platform = ClientPlatform.Unidentified
    )
    {
        var device = await db.AuthClients.FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.AccountId == accountId);
        if (device is not null) return device;
        device = new AuthClient
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

    public string CreateToken(AuthSession session)
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
    public async Task<string> CreateSessionAndIssueToken(AuthChallenge challenge)
    {
        if (challenge.StepRemain != 0)
            throw new ArgumentException("Challenge not yet completed.");

        var hasSession = await db.AuthSessions
            .AnyAsync(e => e.ChallengeId == challenge.Id);
        if (hasSession)
            throw new ArgumentException("Session already exists for this challenge.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var session = new AuthSession
        {
            LastGrantedAt = now,
            ExpiredAt = now.Plus(Duration.FromDays(7)),
            AccountId = challenge.AccountId,
            ChallengeId = challenge.Id
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

    private string CreateCompactToken(Guid sessionId, RSA rsa)
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

    public async Task<bool> ValidateSudoMode(AuthSession session, string? pinCode)
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

    public async Task<ApiKey?> GetApiKey(Guid id, Guid? accountId = null)
    {
        var key = await db.ApiKeys
            .Include(e => e.Session)
            .Where(e => e.Id == id)
            .If(accountId.HasValue, q => q.Where(e => e.AccountId == accountId!.Value))
            .FirstOrDefaultAsync();
        return key;
    }

    public async Task<ApiKey> CreateApiKey(Guid accountId, string label, Instant? expiredAt = null)
    {
        var key = new ApiKey
        {
            AccountId = accountId,
            Label = label,
            Session = new AuthSession
            {
                AccountId = accountId,
                ExpiredAt = expiredAt
            },
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        return key;
    }

    public async Task<string> IssueApiKeyToken(ApiKey key)
    {
        key.Session.LastGrantedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(key.Session);
        await db.SaveChangesAsync();
        var tk = CreateToken(key.Session);
        return tk;
    }

    public async Task RevokeApiKeyToken(ApiKey key)
    {
        db.Remove(key);
        db.Remove(key.Session);
        await db.SaveChangesAsync();
    }

    public async Task<ApiKey> RotateApiKeyToken(ApiKey key)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var oldSessionId = key.SessionId;

            // Create new session
            var newSession = new AuthSession
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

            // Delete old session
            await db.AuthSessions.Where(s => s.Id == oldSessionId).ExecuteDeleteAsync();

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
}