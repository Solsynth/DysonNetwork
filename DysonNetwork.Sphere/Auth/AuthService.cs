using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Sphere.Auth;

public class SignedTokenPair
{
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public Instant ExpiredAt { get; set; }
}

public class AuthService(AppDatabase db, IConfiguration config)
{
    public SignedTokenPair CreateToken(Session session)
    {
        var privateKeyPem = File.ReadAllText(config["Jwt:PrivateKeyPath"]!);
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var key = new RsaSecurityKey(rsa);

        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var accessTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: new List<Claim>
            {
                new("user_id", session.Account.Id.ToString()),
                new("session_id", session.Id.ToString())
            },
            expires: DateTime.Now.AddMinutes(30),
            signingCredentials: creds
        );
        var refreshTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: new List<Claim>
            {
                new("user_id", session.Account.Id.ToString()),
                new("session_id", session.Id.ToString())
            },
            expires: DateTime.Now.AddDays(30),
            signingCredentials: creds
        );

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(accessTokenClaims);
        var refreshToken = handler.WriteToken(refreshTokenClaims);

        return new SignedTokenPair
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddMinutes(30))
        };
    }
}