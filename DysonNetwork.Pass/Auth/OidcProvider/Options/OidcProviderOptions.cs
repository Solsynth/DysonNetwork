using System.Security.Cryptography;

namespace DysonNetwork.Pass.Auth.OidcProvider.Options;

public class OidcProviderOptions
{
    public string IssuerUri { get; set; } = "https://your-issuer-uri.com";
    public string? PublicKeyPath { get; set; }
    public string? PrivateKeyPath { get; set; }
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public bool RequireHttpsMetadata { get; set; } = true;

    public RSA? GetRsaPrivateKey()
    {
        if (string.IsNullOrEmpty(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
            return null;

        var privateKey = File.ReadAllText(PrivateKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey.AsSpan());
        return rsa;
    }

    public RSA? GetRsaPublicKey()
    {
        if (string.IsNullOrEmpty(PublicKeyPath) || !File.Exists(PublicKeyPath))
            return null;

        var publicKey = File.ReadAllText(PublicKeyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey.AsSpan());
        return rsa;
    }
}