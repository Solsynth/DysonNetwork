using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DysonNetwork.Shared.Models;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

public sealed class AuthJwtService(IConfiguration config)
{
    private readonly Lazy<RSA> _privateKey = new(() =>
    {
        var path = config["AuthToken:PrivateKeyPath"] ??
                   throw new InvalidOperationException("AuthToken:PrivateKeyPath is not configured.");
        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lazy<RSA> _publicKey = new(() =>
    {
        var path = config["AuthToken:PublicKeyPath"] ??
                   throw new InvalidOperationException("AuthToken:PublicKeyPath is not configured.");
        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private string Issuer => config["Authentication:Schemes:Bearer:ValidIssuer"] ?? "solar-network";

    private string Audience => config.GetSection("Authentication:Schemes:Bearer:ValidAudiences").Get<string[]>()?.FirstOrDefault()
                               ?? "solar-network";

    public string CreateUserToken(
        SnAuthSession session,
        SnAccount account,
        int accountVersion,
        Instant? expiresAtOverride = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiresAt = expiresAtOverride ?? session.ExpiredAt ?? now.Plus(Duration.FromHours(1));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, session.Id.ToString()),
            new("sid", session.Id.ToString()),
            new("token_use", "user"),
            new("ver", accountVersion.ToString()),
            new("is_superuser", account.IsSuperuser ? "1" : "0"),
            new("name", account.Name),
            new("nick", account.Nick),
            new("region", account.Region),
            new("perk_level", account.PerkLevel.ToString()),
        };
        if (!string.IsNullOrWhiteSpace(account.PerkSubscription?.Identifier))
            claims.Add(new Claim("perk_identifier", account.PerkSubscription.Identifier));
        if (account.PerkSubscription is not null)
            claims.Add(new Claim("perk_subscription_id", account.PerkSubscription.Id.ToString()));
        claims.AddRange(session.Scopes.Select(scope => new Claim("scope", scope)));

        return CreateJwt(claims, now, expiresAt);
    }

    public string CreateRefreshToken(SnAuthSession session, int accountVersion, Instant? expiresAtOverride = null)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiresAt = expiresAtOverride ?? session.ExpiredAt ?? now.Plus(Duration.FromDays(30));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, session.AccountId.ToString()),
            new(JwtRegisteredClaimNames.Jti, session.Id.ToString()),
            new("sid", session.Id.ToString()),
            new("token_use", "refresh"),
            new("ver", accountVersion.ToString())
        };

        return CreateJwt(claims, now, expiresAt);
    }

    public string CreateBotToken(SnApiKey key, SnAuthSession session, int accountVersion)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiresAt = session.ExpiredAt ?? now.Plus(Duration.FromDays(30));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, key.AccountId.ToString()),
            new(JwtRegisteredClaimNames.Jti, session.Id.ToString()),
            new("sid", session.Id.ToString()),
            new("token_use", "api_key"),
            new("api_key_id", key.Id.ToString()),
            new("account_id", key.AccountId.ToString()),
            new("ver", accountVersion.ToString())
        };

        return CreateJwt(claims, now, expiresAt);
    }

    public (bool IsValid, JwtSecurityToken? Token) ValidateJwt(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(_publicKey.Value),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };

            handler.ValidateToken(token, parameters, out var validated);
            return (true, validated as JwtSecurityToken);
        }
        catch
        {
            return (false, null);
        }
    }

    private string CreateJwt(IEnumerable<Claim> claims, Instant issuedAt, Instant expiresAt)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: issuedAt.ToDateTimeUtc(),
            expires: expiresAt.ToDateTimeUtc(),
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(_privateKey.Value),
                SecurityAlgorithms.RsaSha256
            )
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
