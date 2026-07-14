using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DysonNetwork.Padlock.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DysonNetwork.Padlock.Tests.Auth;

public sealed class AuthJwtServiceTests : IDisposable
{
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly AuthJwtService _service;

    public AuthJwtServiceTests()
    {
        Directory.CreateDirectory(_keyDirectory);
        var privateKeyPath = Path.Combine(_keyDirectory, "private.pem");
        var publicKeyPath = Path.Combine(_keyDirectory, "public.pem");
        File.WriteAllText(privateKeyPath, _rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(publicKeyPath, _rsa.ExportRSAPublicKeyPem());

        var values = new Dictionary<string, string?>
        {
            ["AuthToken:PrivateKeyPath"] = privateKeyPath,
            ["AuthToken:PublicKeyPath"] = publicKeyPath,
            ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audience,
        };
        _service = new AuthJwtService(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    public void ValidateJwt_RejectsSignedTokenBeforeNotBefore()
    {
        var token = CreateToken("user", DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow.AddHours(1));

        var result = _service.ValidateJwt(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateJwt_RejectsExpiredUserToken()
    {
        var token = CreateToken("user", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-2));

        var result = _service.ValidateJwt(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateJwt_PreservesApiKeySessionExpiryCompatibility()
    {
        var token = CreateToken("api_key", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-2));

        var result = _service.ValidateJwt(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Token);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        Directory.Delete(_keyDirectory, true);
    }

    private string CreateToken(string type, DateTime notBefore, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(AuthJwtService.ClaimType, type)],
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
