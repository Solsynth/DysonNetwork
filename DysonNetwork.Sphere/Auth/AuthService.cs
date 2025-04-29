using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Casbin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Sphere.Auth;

public class SignedTokenPair
{
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public Instant ExpiredAt { get; set; }
}

public class AuthService(IConfiguration config, IHttpClientFactory httpClientFactory)
{
    public async Task<bool> ValidateCaptcha(string token)
    {
        var provider = config.GetSection("Captcha")["Provider"]?.ToLower();
        var apiSecret = config.GetSection("Captcha")["ApiSecret"];

        var client = httpClientFactory.CreateClient();

        switch (provider)
        {
            case "cloudflare":
                var content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
                    content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var cfResult = JsonSerializer.Deserialize<CloudflareVerificationResponse>(json);

                return cfResult?.Success == true;
            case "google":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
                response.EnsureSuccessStatusCode();

                json = await response.Content.ReadAsStringAsync();
                var capResult = JsonSerializer.Deserialize<GoogleVerificationResponse>(json);

                return capResult?.Success == true;
            default:
                throw new ArgumentException("The server misconfigured for the captcha.");
        }
    }

    public SignedTokenPair CreateToken(Session session)
    {
        var privateKeyPem = File.ReadAllText(config["Jwt:PrivateKeyPath"]!);
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var key = new RsaSecurityKey(rsa);

        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new("user_id", session.Account.Id.ToString()),
            new("session_id", session.Id.ToString())
        };

        var refreshTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: claims,
            expires: DateTime.Now.AddDays(30),
            signingCredentials: creds
        );

        session.Challenge.Scopes.ForEach(c => claims.Add(new Claim("scope", c)));
        if (session.Account.IsSuperuser) claims.Add(new Claim("is_superuser", "1"));
        var accessTokenClaims = new JwtSecurityToken(
            issuer: "solar-network",
            audience: string.Join(',', session.Challenge.Audiences),
            claims: claims,
            expires: DateTime.Now.AddMinutes(30),
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