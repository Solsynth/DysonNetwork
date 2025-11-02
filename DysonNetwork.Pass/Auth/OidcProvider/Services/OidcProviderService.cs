using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Pass.Auth.OidcProvider.Models;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using DysonNetwork.Pass.Auth.OidcProvider.Responses;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using AccountContactType = DysonNetwork.Shared.Models.AccountContactType;

namespace DysonNetwork.Pass.Auth.OidcProvider.Services;

public class OidcProviderService(
    AppDatabase db,
    AuthService auth,
    CustomAppService.CustomAppServiceClient customApps,
    ICacheService cache,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderService> logger
)
{
    private readonly OidcProviderOptions _options = options.Value;

    private const string CacheKeyPrefixClientId = "auth:oidc-client:id:";
    private const string CacheKeyPrefixClientSlug = "auth:oidc-client:slug:";
    private const string CacheKeyPrefixAuthCode = "auth:oidc-code:";
    private const string CodeChallengeMethodS256 = "S256";
    private const string CodeChallengeMethodPlain = "PLAIN";

    public async Task<CustomApp?> FindClientByIdAsync(Guid clientId)
    {
        var cacheKey = $"{CacheKeyPrefixClientId}{clientId}";
        var (found, cachedApp) = await cache.GetAsyncWithStatus<CustomApp>(cacheKey);
        if (found && cachedApp != null)
        {
            return cachedApp;
        }

        var resp = await customApps.GetCustomAppAsync(new GetCustomAppRequest { Id = clientId.ToString() });
        if (resp.App != null)
        {
            await cache.SetAsync(cacheKey, resp.App, TimeSpan.FromMinutes(5));
        }
        return resp.App ?? null;
    }

    public async Task<CustomApp?> FindClientBySlugAsync(string slug)
    {
        var cacheKey = $"{CacheKeyPrefixClientSlug}{slug}";
        var (found, cachedApp) = await cache.GetAsyncWithStatus<CustomApp>(cacheKey);
        if (found && cachedApp != null)
        {
            return cachedApp;
        }

        var resp = await customApps.GetCustomAppAsync(new GetCustomAppRequest { Slug = slug });
        if (resp.App != null)
        {
            await cache.SetAsync(cacheKey, resp.App, TimeSpan.FromMinutes(5));
        }
        return resp.App ?? null;
    }

    private async Task<SnAuthSession?> FindValidSessionAsync(Guid accountId, Guid clientId, bool withAccount = false)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var queryable = db.AuthSessions
            .Include(s => s.Challenge)
            .AsQueryable();
        if (withAccount)
            queryable = queryable
                .Include(s => s.Account)
                .ThenInclude(a => a.Profile)
                .Include(a => a.Account.Contacts)
                .AsQueryable();

        return await queryable
            .Where(s => s.AccountId == accountId &&
                        s.AppId == clientId &&
                        (s.ExpiredAt == null || s.ExpiredAt > now) &&
                        s.Challenge != null &&
                        s.Challenge.Type == Shared.Models.ChallengeType.OAuth)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> ValidateClientCredentialsAsync(Guid clientId, string clientSecret)
    {
        var resp = await customApps.CheckCustomAppSecretAsync(new CheckCustomAppSecretRequest
        {
            AppId = clientId.ToString(),
            Secret = clientSecret,
            IsOidc = true
        });
        return resp.Valid;
    }

    private static bool IsWildcardRedirectUriMatch(string allowedUri, string redirectUri)
    {
        if (string.IsNullOrEmpty(allowedUri) || string.IsNullOrEmpty(redirectUri))
            return false;

        // Check if it's an exact match
        if (string.Equals(allowedUri, redirectUri, StringComparison.Ordinal))
            return true;

        // Quick check for wildcard patterns
        if (!allowedUri.Contains('*'))
            return false;

        // Parse URIs once
        Uri? allowedUriObj, redirectUriObj;
        try
        {
            allowedUriObj = new Uri(allowedUri);
            redirectUriObj = new Uri(redirectUri);
        }
        catch (UriFormatException)
        {
            return false;
        }

        // Check scheme and port matches
        if (allowedUriObj.Scheme != redirectUriObj.Scheme || allowedUriObj.Port != redirectUriObj.Port)
        {
            return false;
        }

        var allowedHost = allowedUriObj.Host;
        var redirectHost = redirectUriObj.Host;

        // Handle wildcard domain patterns like *.example.com
        if (allowedHost.StartsWith("*."))
        {
            var baseDomain = allowedHost[2..]; // Remove "*."
            if (redirectHost == baseDomain || redirectHost.EndsWith("." + baseDomain))
            {
                // Check path match
                var allowedPath = allowedUriObj.AbsolutePath.TrimEnd('/');
                var redirectPath = redirectUriObj.AbsolutePath.TrimEnd('/');

                // If allowed path is empty, any path is allowed
                // If allowed path is specified, redirect path must start with it
                return string.IsNullOrEmpty(allowedPath) ||
                       redirectPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    public async Task<bool> ValidateRedirectUriAsync(Guid clientId, string redirectUri)
    {
        if (string.IsNullOrEmpty(redirectUri))
            return false;

        var client = await FindClientByIdAsync(clientId);
        if (client?.Status != Shared.Proto.CustomAppStatus.Production)
            return true;

        var redirectUris = client?.OauthConfig?.RedirectUris;
        if (redirectUris == null || redirectUris.Count == 0)
            return false;

        // Check each allowed URI for a match
        foreach (var allowedUri in redirectUris)
        {
            if (IsWildcardRedirectUriMatch(allowedUri, redirectUri))
            {
                return true;
            }
        }

        return false;
    }

    private string GenerateIdToken(
        CustomApp client,
        SnAuthSession session,
        string? nonce = null,
        IEnumerable<string>? scopes = null
    )
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var clock = SystemClock.Instance;
        var now = clock.GetCurrentInstant();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Iss, _options.IssuerUri),
            new(JwtRegisteredClaimNames.Sub, session.AccountId.ToString()),
            new(JwtRegisteredClaimNames.Aud, client.Slug),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp,
                now.Plus(Duration.FromSeconds(_options.AccessTokenLifetime.TotalSeconds)).ToUnixTimeSeconds()
                    .ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.AuthTime, session.CreatedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        // Add nonce if provided (required for implicit and hybrid flows)
        if (!string.IsNullOrEmpty(nonce))
        {
            claims.Add(new Claim("nonce", nonce));
        }

        // Add email claim if email scope is requested
        var scopesList = scopes?.ToList() ?? [];
        if (scopesList.Contains("email"))
        {
            var contact = session.Account.Contacts.FirstOrDefault(c => c.Type == AccountContactType.Email);
            if (contact is not null)
            {
                claims.Add(new Claim(JwtRegisteredClaimNames.Email, contact.Content));
                claims.Add(new Claim("email_verified", contact.VerifiedAt is not null ? "true" : "false",
                    ClaimValueTypes.Boolean));
            }
        }

        // Add profile claims if profile scope is requested
        if (scopes != null && scopesList.Contains("profile"))
        {
            if (!string.IsNullOrEmpty(session.Account.Name))
                claims.Add(new Claim("preferred_username", session.Account.Name));
            if (!string.IsNullOrEmpty(session.Account.Nick))
                claims.Add(new Claim("name", session.Account.Nick));
            if (!string.IsNullOrEmpty(session.Account.Profile.FirstName))
                claims.Add(new Claim("given_name", session.Account.Profile.FirstName));
            if (!string.IsNullOrEmpty(session.Account.Profile.LastName))
                claims.Add(new Claim("family_name", session.Account.Profile.LastName));
        }

        claims.Add(new Claim(JwtRegisteredClaimNames.Azp, client.Slug));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.IssuerUri,
            Audience = client.Slug.ToString(),
            Expires = now.Plus(Duration.FromSeconds(_options.AccessTokenLifetime.TotalSeconds)).ToDateTimeUtc(),
            NotBefore = now.ToDateTimeUtc(),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_options.GetRsaPrivateKey()),
                SecurityAlgorithms.RsaSha256
            )
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private async Task<(SnAuthSession session, string? nonce, List<string>? scopes)> HandleAuthorizationCodeFlowAsync(
        string authorizationCode,
        Guid clientId,
        string? redirectUri,
        string? codeVerifier
    )
    {
        var authCode = await ValidateAuthorizationCodeAsync(authorizationCode, clientId, redirectUri, codeVerifier);
        if (authCode == null)
            throw new InvalidOperationException("Invalid authorization code");

        // Load the session for the user
        var existingSession = await FindValidSessionAsync(authCode.AccountId, clientId, withAccount: true);

        SnAuthSession session;
        if (existingSession == null)
        {
            var account = await db.Accounts
                .Where(a => a.Id == authCode.AccountId)
                .Include(a => a.Profile)
                .Include(a => a.Contacts)
                .FirstOrDefaultAsync();
            if (account == null) throw new InvalidOperationException("Account not found");
            session = await auth.CreateSessionForOidcAsync(account, SystemClock.Instance.GetCurrentInstant(), clientId);
            session.Account = account;
        }
        else
        {
            session = existingSession;
        }

        return (session, authCode.Nonce, authCode.Scopes);
    }

    private async Task<(SnAuthSession session, string? nonce, List<string>? scopes)> HandleRefreshTokenFlowAsync(Guid sessionId)
    {
        var session = await FindSessionByIdAsync(sessionId) ??
                      throw new InvalidOperationException("Session not found");

        // Verify the session is still valid
        var now = SystemClock.Instance.GetCurrentInstant();
        if (session.ExpiredAt.HasValue && session.ExpiredAt < now)
            throw new InvalidOperationException("Session has expired");

        return (session, null, null);
    }

    public async Task<TokenResponse> GenerateTokenResponseAsync(
        Guid clientId,
        string? authorizationCode = null,
        string? redirectUri = null,
        string? codeVerifier = null,
        Guid? sessionId = null
    )
    {
        if (clientId == Guid.Empty) throw new ArgumentException("Client ID cannot be empty", nameof(clientId));

        var client = await FindClientByIdAsync(clientId) ?? throw new InvalidOperationException("Client not found");

        var (session, nonce, scopes) = authorizationCode != null
            ? await HandleAuthorizationCodeFlowAsync(authorizationCode, clientId, redirectUri, codeVerifier)
            : sessionId.HasValue
                ? await HandleRefreshTokenFlowAsync(sessionId.Value)
                : throw new InvalidOperationException("Either authorization code or session ID must be provided");

        var clock = SystemClock.Instance;
        var now = clock.GetCurrentInstant();
        var expiresIn = (int)_options.AccessTokenLifetime.TotalSeconds;
        var expiresAt = now.Plus(Duration.FromSeconds(expiresIn));

        // Generate tokens
        var accessToken = GenerateJwtToken(client, session, expiresAt, scopes);
        var idToken = GenerateIdToken(client, session, nonce, scopes);
        var refreshToken = GenerateRefreshToken(session);

        return new TokenResponse
        {
            AccessToken = accessToken,
            IdToken = idToken,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            RefreshToken = refreshToken,
            Scope = scopes != null ? string.Join(" ", scopes) : null
        };
    }

    private string GenerateJwtToken(
        CustomApp client,
        SnAuthSession session,
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
                new Claim(JwtRegisteredClaimNames.Azp, client.Slug),
            ]),
            Expires = expiresAt.ToDateTimeUtc(),
            Issuer = _options.IssuerUri,
            Audience = client.Slug
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

    public async Task<SnAuthSession?> FindSessionByIdAsync(Guid sessionId)
    {
        return await db.AuthSessions
            .Include(s => s.Account)
            .Include(s => s.Challenge)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    private static string GenerateRefreshToken(SnAuthSession session)
    {
        return Convert.ToBase64String(session.Id.ToByteArray());
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
        var cacheKey = $"{CacheKeyPrefixAuthCode}{code}";
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
        var cacheKey = $"{CacheKeyPrefixAuthCode}{code}";
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
                CodeChallengeMethodS256 => VerifyCodeChallenge(codeVerifier, authCode.CodeChallenge, CodeChallengeMethodS256),
                CodeChallengeMethodPlain => VerifyCodeChallenge(codeVerifier, authCode.CodeChallenge, CodeChallengeMethodPlain),
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

        if (method != CodeChallengeMethodS256)
            return method == CodeChallengeMethodPlain &&
                   string.Equals(codeVerifier, codeChallenge, StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        var base64 = Base64UrlEncoder.Encode(hash);

        return string.Equals(base64, codeChallenge, StringComparison.Ordinal);
    }
}
