using System.Security.Cryptography;

namespace DysonNetwork.Passport.Auth;

public class AuthTokenKeyProvider(IConfiguration config)
{
    private readonly Lazy<RSAParameters> _privateKey = new(() =>
    {
        var path = config["AuthToken:PrivateKeyPath"] ?? throw new InvalidOperationException("AuthToken:PrivateKeyPath is not configured.");
        var pem = File.ReadAllText(path);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.ExportParameters(true);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lazy<RSAParameters> _publicKey = new(() =>
    {
        var path = config["AuthToken:PublicKeyPath"] ?? throw new InvalidOperationException("AuthToken:PublicKeyPath is not configured.");
        var pem = File.ReadAllText(path);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.ExportParameters(false);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public string CreateCompactToken(Guid sessionId)
    {
        var payload = sessionId.ToByteArray();
        using var rsa = RSA.Create();
        rsa.ImportParameters(_privateKey.Value);

        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
    }

    public bool TryValidateCompactToken(string token, out Guid sessionId)
    {
        sessionId = Guid.Empty;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return false;
        }

        if (payload.Length != 16) return false;

        using var rsa = RSA.Create();
        rsa.ImportParameters(_publicKey.Value);
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            return false;

        sessionId = new Guid(payload);
        return true;
    }

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
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }
}
