using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Pass.Auth.OidcProvider.Models;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using DysonNetwork.Pass.Auth.OidcProvider.Responses;
using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Pass.Auth.OidcProvider.Services;

public class OidcProviderService(
    AppDatabase db,
    AuthService auth,
    ICacheService cache,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderService> logger
)
{
    private readonly OidcProviderOptions _options = options.Value;

    public async Task<CustomApp?> FindClientByIdAsync(Guid clientId)
    {
        return await db.CustomApps
            .Include(c => c.Secrets)
            .FirstOrDefaultAsync(c => c.Id == clientId);
    }

    public async Task<CustomApp?> FindClientByAppIdAsync(Guid appId)
    {
        return await db.CustomApps
            .Include(c => c.Secrets)
            .FirstOrDefaultAsync(c => c.Id == appId);
    }

    public async Task<Session?> FindValidSessionAsync(Guid accountId, Guid clientId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        return await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.AccountId == accountId &&
                        s.AppId == clientId &&
                        (s.ExpiredAt == null || s.ExpiredAt > now) &&
                        s.Challenge.Type == ChallengeType.OAuth)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> ValidateClientCredentialsAsync(Guid clientId, string clientSecret)
    {
        var client = await FindClientByIdAsync(clientId);
        if (client == null) return false;

        var clock = SystemClock.Instance;
        var secret = client.Secrets
            .Where(s => s.IsOidc && (s.ExpiredAt == null || s.ExpiredAt > clock.GetCurrentInstant()))
            .FirstOrDefault(s => s.Secret == clientSecret); // In production, use proper hashing

        return secret != null;
    }

    public async Task<TokenResponse> GenerateTokenResponseAsync(
        Guid clientId,
        string? authorizationCode = null,
        string? redirectUri = null,
        string? codeVerifier = null,
        Guid? sessionId = null
    )
    {
        var client = await FindClientByIdAsync(clientId);
        if (client == null)
            throw new InvalidOperationException("Client not found");

        Session session;
        var clock = SystemClock.Instance;
        var now = clock.GetCurrentInstant();

        List<string>? scopes = null;
        if (authorizationCode != null)
        {
            // Authorization code flow
            var authCode = await ValidateAuthorizationCodeAsync(authorizationCode, clientId, redirectUri, codeVerifier);
            if (authCode is null) throw new InvalidOperationException("Invalid authorization code");
            var account = await db.Accounts.Where(a => a.Id == authCode.AccountId).FirstOrDefaultAsync();
            if (account is null) throw new InvalidOperationException("Account was not found");

            session = await auth.CreateSessionForOidcAsync(account, now, client.Id);
            scopes = authCode.Scopes;
        }
        else if (sessionId.HasValue)
        {
            // Refresh token flow
            session = await FindSessionByIdAsync(sessionId.Value) ??
                      throw new InvalidOperationException("Invalid session");

            // Verify the session is still valid
            if (session.ExpiredAt < now)
                throw new InvalidOperationException("Session has expired");
        }
        else
        {
            throw new InvalidOperationException("Either authorization code or session ID must be provided");
        }

        var expiresIn = (int)_options.AccessTokenLifetime.TotalSeconds;
        var expiresAt = now.Plus(Duration.FromSeconds(expiresIn));

        // Generate an access token
        var accessToken = GenerateJwtToken(client, session, expiresAt, scopes);
        var refreshToken = GenerateRefreshToken(session);

        return new TokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            RefreshToken = refreshToken,
            Scope = scopes != null ? string.Join(" ", scopes) : null
        };
    }

    private string GenerateJwtToken(
        CustomApp client,
        Session session,
        Instant expiresAt,
        IEnumerable<string>? scopes = null
    )
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var clock = SystemClock.Instance;
        var now = clock.GetCurrentInstant();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, session.AccountId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, session.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
                new Claim("client_id", client.Id.ToString())
            ]),
            Expires = expiresAt.ToDateTimeUtc(),
            Issuer = _options.IssuerUri,
            Audience = client.Id.ToString()
        };

        // Try to use RSA signing if keys are available, fall back to HMAC
        var rsaPrivateKey = _options.GetRsaPrivateKey();
        tokenDescriptor.SigningCredentials = new SigningCredentials(
            new RsaSecurityKey(rsaPrivateKey),
            SecurityAlgorithms.RsaSha256
        );

        // Add scopes as claims if provided
        var effectiveScopes = scopes?.ToList() ?? client.OauthConfig!.AllowedScopes?.ToList() ?? [];
        if (effectiveScopes.Count != 0)
        {
            tokenDescriptor.Subject.AddClaims(
                effectiveScopes.Select(scope => new Claim("scope", scope)));
        }

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public (bool isValid, JwtSecurityToken? token) ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.IssuerUri,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            // Try to use RSA validation if public key is available
            var rsaPublicKey = _options.GetRsaPublicKey();
            validationParameters.IssuerSigningKey = new RsaSecurityKey(rsaPublicKey);
            validationParameters.ValidateIssuerSigningKey = true;
            validationParameters.ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 };


            tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            return (true, (JwtSecurityToken)validatedToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token validation failed");
            return (false, null);
        }
    }

    public async Task<Session?> FindSessionByIdAsync(Guid sessionId)
    {
        return await db.AuthSessions
            .Include(s => s.Account)
            .Include(s => s.Challenge)
            .Include(s => s.App)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    private static string GenerateRefreshToken(Session session)
    {
        return Convert.ToBase64String(session.Id.ToByteArray());
    }

    private static bool VerifyHashedSecret(string secret, string hashedSecret)
    {
        // In a real implementation, you'd use a proper password hashing algorithm like PBKDF2, bcrypt, or Argon2
        // For now, we'll do a simple comparison, but you should replace this with proper hashing
        return string.Equals(secret, hashedSecret, StringComparison.Ordinal);
    }

    public async Task<string> GenerateAuthorizationCodeForReuseSessionAsync(
        Session session,
        Guid clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        string? codeChallenge = null,
        string? codeChallengeMethod = null,
        string? nonce = null)
    {
        var clock = SystemClock.Instance;
        var now = clock.GetCurrentInstant();
        var code = Guid.NewGuid().ToString("N");

        // Update the session's last activity time
        await db.AuthSessions.Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.LastGrantedAt, now));

        // Create the authorization code info
        var authCodeInfo = new AuthorizationCodeInfo
        {
            ClientId = clientId,
            AccountId = session.AccountId,
            RedirectUri = redirectUri,
            Scopes = scopes.ToList(),
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Nonce = nonce,
            CreatedAt = now
        };
        
        // Store the code with its metadata in the cache
        var cacheKey = $"auth:code:{code}";
        await cache.SetAsync(cacheKey, authCodeInfo, _options.AuthorizationCodeLifetime);

        logger.LogInformation("Generated authorization code for client {ClientId} and user {UserId}", clientId, session.AccountId);
        return code;
    }

    public async Task<string> GenerateAuthorizationCodeAsync(
        Guid clientId,
        Guid userId,
        string redirectUri,
        IEnumerable<string> scopes,
        string? codeChallenge = null,
        string? codeChallengeMethod = null,
        string? nonce = null
    )
    {
        // Generate a random code
        var clock = SystemClock.Instance;
        var code = GenerateRandomString(32);
        var now = clock.GetCurrentInstant();

        // Create the authorization code info
        var authCodeInfo = new AuthorizationCodeInfo
        {
            ClientId = clientId,
            AccountId = userId,
            RedirectUri = redirectUri,
            Scopes = scopes.ToList(),
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Nonce = nonce,
            CreatedAt = now
        };

        // Store the code with its metadata in the cache
        var cacheKey = $"auth:code:{code}";
        await cache.SetAsync(cacheKey, authCodeInfo, _options.AuthorizationCodeLifetime);

        logger.LogInformation("Generated authorization code for client {ClientId} and user {UserId}", clientId, userId);
        return code;
    }

    private async Task<AuthorizationCodeInfo?> ValidateAuthorizationCodeAsync(
        string code,
        Guid clientId,
        string? redirectUri = null,
        string? codeVerifier = null
    )
    {
        var cacheKey = $"auth:code:{code}";
        var (found, authCode) = await cache.GetAsyncWithStatus<AuthorizationCodeInfo>(cacheKey);

        if (!found || authCode == null)
        {
            logger.LogWarning("Authorization code not found: {Code}", code);
            return null;
        }

        // Verify client ID matches
        if (authCode.ClientId != clientId)
        {
            logger.LogWarning(
                "Client ID mismatch for code {Code}. Expected: {ExpectedClientId}, Actual: {ActualClientId}",
                code, authCode.ClientId, clientId);
            return null;
        }

        // Verify redirect URI if provided
        if (!string.IsNullOrEmpty(redirectUri) && authCode.RedirectUri != redirectUri)
        {
            logger.LogWarning("Redirect URI mismatch for code {Code}", code);
            return null;
        }

        // Verify PKCE code challenge if one was provided during authorization
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            if (string.IsNullOrEmpty(codeVerifier))
            {
                logger.LogWarning("PKCE code verifier is required but not provided for code {Code}", code);
                return null;
            }

            var isValid = authCode.CodeChallengeMethod?.ToUpperInvariant() switch
            {
                "S256" => VerifyCodeChallenge(codeVerifier, authCode.CodeChallenge, "S256"),
                "PLAIN" => VerifyCodeChallenge(codeVerifier, authCode.CodeChallenge, "PLAIN"),
                _ => false // Unsupported code challenge method
            };

            if (!isValid)
            {
                logger.LogWarning("PKCE code verifier validation failed for code {Code}", code);
                return null;
            }
        }

        // Code is valid, remove it from the cache (codes are single-use)
        await cache.RemoveAsync(cacheKey);

        return authCode;
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
        var random = RandomNumberGenerator.Create();
        var result = new char[length];

        for (int i = 0; i < length; i++)
        {
            var randomNumber = new byte[4];
            random.GetBytes(randomNumber);
            var index = (int)(BitConverter.ToUInt32(randomNumber, 0) % chars.Length);
            result[i] = chars[index];
        }

        return new string(result);
    }

    private static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge, string method)
    {
        if (string.IsNullOrEmpty(codeVerifier)) return false;

        if (method == "S256")
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            var base64 = Base64UrlEncoder.Encode(hash);
            return string.Equals(base64, codeChallenge, StringComparison.Ordinal);
        }

        if (method == "PLAIN")
        {
            return string.Equals(codeVerifier, codeChallenge, StringComparison.Ordinal);
        }

        return false;
    }
}