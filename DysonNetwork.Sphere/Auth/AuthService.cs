using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Auth;

public class AuthService(AppDatabase db, IConfiguration config, IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext!;

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
            if (!string.IsNullOrEmpty(lastActiveInfo?.Challenge.IpAddress) &&
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
        var totalRequiredSteps = (int)Math.Round((float)maxSteps * riskScore / 3);
        // Clamp the steps
        totalRequiredSteps = Math.Max(Math.Min(totalRequiredSteps, maxSteps), 1);

        return totalRequiredSteps;
    }

    public async Task<Session> CreateSessionAsync(Account.Account account, Instant time)
    {
        var challenge = new Challenge
        {
            AccountId = account.Id,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent,
            StepRemain = 1,
            StepTotal = 1,
            Type = ChallengeType.Oidc
        };

        var session = new Session
        {
            AccountId = account.Id,
            CreatedAt = time,
            LastGrantedAt = time,
            Challenge = challenge
        };

        db.AuthChallenges.Add(challenge);
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    public async Task<bool> ValidateCaptcha(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var provider = config.GetSection("Captcha")["Provider"]?.ToLower();
        var apiSecret = config.GetSection("Captcha")["ApiSecret"];

        var client = httpClientFactory.CreateClient();

        var jsonOpts = new JsonSerializerOptions
        {
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
                response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
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

    public string CreateToken(Session session)
    {
        // Load the private key for signing
        var privateKeyPem = File.ReadAllText(config["Jwt:PrivateKeyPath"]!);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        // Create and return a single token
        return CreateCompactToken(session.Id, rsa);
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

    public bool ValidateToken(string token, out Guid sessionId)
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
            var publicKeyPem = File.ReadAllText(config["Jwt:PublicKeyPath"]!);
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
}