using System.Security.Cryptography;

namespace DysonNetwork.Sphere.Auth;

public class CompactTokenService(IConfiguration config)
{
    private readonly string _privateKeyPath = config["Jwt:PrivateKeyPath"] 
        ?? throw new InvalidOperationException("Jwt:PrivateKeyPath configuration is missing");
    
    public string CreateToken(Session session)
    {
        // Load the private key for signing
        var privateKeyPem = File.ReadAllText(_privateKeyPath);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        
        // Create and return a single token
        return CreateCompactToken(session.Id, rsa);
    }

    private string CreateCompactToken(Guid sessionId, RSA rsa)
    {
        // Create the payload: just the session ID
        var payloadBytes = sessionId.ToByteArray();
        
        // Base64Url encode the payload
        var payloadBase64 = Base64UrlEncode(payloadBytes);
        
        // Sign the payload with RSA-SHA256
        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        
        // Base64Url encode the signature
        var signatureBase64 = Base64UrlEncode(signature);
        
        // Combine payload and signature with a dot
        return $"{payloadBase64}.{signatureBase64}";
    }
    
    public bool ValidateToken(string token, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        
        try
        {
            // Split the token
            var parts = token.Split('.');
            if (parts.Length != 2)
                return false;
            
            // Decode the payload
            var payloadBytes = Base64UrlDecode(parts[0]);
            
            // Extract session ID
            sessionId = new Guid(payloadBytes);
            
            // Load public key for verification
            var publicKeyPem = File.ReadAllText(config["Jwt:PublicKeyPath"]!);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            
            // Verify signature
            var signature = Base64UrlDecode(parts[1]);
            return rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
    
    // Helper methods for Base64Url encoding/decoding
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    
    private static byte[] Base64UrlDecode(string base64Url)
    {
        string padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');
            
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        
        return Convert.FromBase64String(padded);
    }
}