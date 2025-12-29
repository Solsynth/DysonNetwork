using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubKeyService(ILogger<ActivityPubKeyService> logger)
{
    public (string privateKeyPem, string publicKeyPem) GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        
        var privateKey = rsa.ExportRSAPrivateKey();
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        
        var privateKeyPem = ConvertToPem(privateKey, "RSA PRIVATE KEY");
        var publicKeyPem = ConvertToPem(publicKey, "PUBLIC KEY");
        
        logger.LogInformation("Generated new RSA key pair for ActivityPub");
        
        return (privateKeyPem, publicKeyPem);
    }

    public static string Sign(string privateKeyPem, string dataToSign)
    {
        using var rsa = CreateRsaFromPrivateKeyPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(dataToSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return Convert.ToBase64String(signature);
    }

    public bool Verify(string publicKeyPem, string data, string signatureBase64)
    {
        try
        {
            using var rsa = CreateRsaFromPublicKeyPem(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(data),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify signature");
            return false;
        }
    }

    private static string ConvertToPem(byte[] keyData, string keyType)
    {
        var sb = new StringBuilder();
        sb.Append($"-----BEGIN {keyType}-----\n");
        sb.Append(Convert.ToBase64String(keyData) + "\n");
        sb.Append($"-----END {keyType}-----");
        return sb.ToString();
    }

    private static RSA CreateRsaFromPrivateKeyPem(string privateKeyPem)
    {
        var rsa = RSA.Create();
        
        var lines = privateKeyPem.Split('\n')
            .Where(line => !line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
            .ToArray();
        
        var keyBytes = Convert.FromBase64String(string.Join("", lines));
        rsa.ImportRSAPrivateKey(keyBytes, out _);
        
        return rsa;
    }

    private static RSA CreateRsaFromPublicKeyPem(string publicKeyPem)
    {
        var rsa = RSA.Create();
        
        var lines = publicKeyPem.Split('\n')
            .Where(line => !line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
            .ToArray();
        
        var keyBytes = Convert.FromBase64String(string.Join("", lines));
        rsa.ImportRSAPublicKey(keyBytes, out _);
        
        return rsa;
    }
}
