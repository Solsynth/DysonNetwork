using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DysonNetwork.Sphere.Auth.OidcProvider.Models;
using DysonNetwork.Sphere.Auth.OidcProvider.Options;
using DysonNetwork.Sphere.Auth.OidcProvider.Responses;
using DysonNetwork.Sphere.Developer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OidcProvider.Services;

public class OidcProviderService(
    AppDatabase db,
    IClock clock,
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

    public async Task<bool> ValidateClientCredentialsAsync(Guid clientId, string clientSecret)
    {
        var client = await FindClientByIdAsync(clientId);
        if (client == null) return false;

        var secret = client.Secrets
            .Where(s => s.IsOidc && (s.ExpiredAt == null || s.ExpiredAt > clock.GetCurrentInstant()))
            .FirstOrDefault(s => s.Secret == clientSecret); // In production, use proper hashing

        return secret != null;
    }

    public async Task<TokenResponse> GenerateTokenResponseAsync(
        Guid clientId,
        string subjectId,
        IEnumerable<string>? scopes = null,
        string? authorizationCode = null)
    {
        var client = await FindClientByIdAsync(clientId);
        if (client == null)
            throw new InvalidOperationException("Client not found");

        var now = clock.GetCurrentInstant();
        var expiresIn = (int)_options.AccessTokenLifetime.TotalSeconds;
        var expiresAt = now.Plus(Duration.FromSeconds(expiresIn));

        // Generate access token
        var accessToken = GenerateJwtToken(client, subjectId, expiresAt, scopes);
        var refreshToken = GenerateRefreshToken();

        // In a real implementation, you would store the token in the database
        // For this example, we'll just return the token without storing it
        // as we don't have a dedicated OIDC token table

        return new TokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            RefreshToken = refreshToken,
            Scope = scopes != null ? string.Join(" ", scopes) : null
        };
    }

    private string GenerateJwtToken(CustomApp client, string subjectId, Instant expiresAt, IEnumerable<string>? scopes = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_options.SigningKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subjectId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, clock.GetCurrentInstant().ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
                new Claim("client_id", client.Id.ToString())
            }),
            Expires = expiresAt.ToDateTimeUtc(),
            Issuer = _options.IssuerUri,
            Audience = client.Id.ToString(),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        // Add scopes as claims if provided, otherwise use client's default scopes
        var effectiveScopes = scopes?.ToList() ?? client.AllowedScopes?.ToList() ?? new List<string>();
        if (effectiveScopes.Any())
        {
            tokenDescriptor.Subject.AddClaims(
                effectiveScopes.Select(scope => new Claim("scope", scope)));
        }

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static bool VerifyHashedSecret(string secret, string hashedSecret)
    {
        // In a real implementation, you'd use a proper password hashing algorithm like PBKDF2, bcrypt, or Argon2
        // For now, we'll do a simple comparison, but you should replace this with proper hashing
        return string.Equals(secret, hashedSecret, StringComparison.Ordinal);
    }

    public JwtSecurityToken? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_options.SigningKey);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _options.IssuerUri,
                ValidateAudience = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out var validatedToken);

            return (JwtSecurityToken)validatedToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token validation failed");
            return null;
        }
    }

    private static readonly Dictionary<string, AuthorizationCodeInfo> _authorizationCodes = new();
    
    public async Task<string> GenerateAuthorizationCodeAsync(
        Guid clientId,
        string userId,
        string redirectUri,
        IEnumerable<string> scopes,
        string? codeChallenge = null,
        string? codeChallengeMethod = null,
        string? nonce = null)
    {
        // Generate a random code
        var code = GenerateRandomString(32);
        var now = clock.GetCurrentInstant();
        
        // Store the code with its metadata
        _authorizationCodes[code] = new AuthorizationCodeInfo
        {
            ClientId = clientId,
            UserId = userId,
            RedirectUri = redirectUri,
            Scopes = scopes.ToList(),
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Nonce = nonce,
            Expiration = now.Plus(Duration.FromTimeSpan(_options.AuthorizationCodeLifetime)),
            CreatedAt = now
        };
        
        logger.LogInformation("Generated authorization code for client {ClientId} and user {UserId}", clientId, userId);
        return code;
    }
    
    public async Task<AuthorizationCodeInfo?> ValidateAuthorizationCodeAsync(
        string code,
        Guid clientId,
        string? redirectUri = null,
        string? codeVerifier = null)
    {
        if (!_authorizationCodes.TryGetValue(code, out var authCode) || authCode == null)
        {
            logger.LogWarning("Authorization code not found: {Code}", code);
            return null;
        }
        
        var now = clock.GetCurrentInstant();
        
        // Check if code has expired
        if (now > authCode.Expiration)
        {
            logger.LogWarning("Authorization code expired: {Code}", code);
            _authorizationCodes.Remove(code);
            return null;
        }
        
        // Verify client ID matches
        if (authCode.ClientId != clientId)
        {
            logger.LogWarning("Client ID mismatch for code {Code}. Expected: {ExpectedClientId}, Actual: {ActualClientId}", 
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
        
        // Code is valid, remove it from the store (codes are single-use)
        _authorizationCodes.Remove(code);
        
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