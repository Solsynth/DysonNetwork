using System.Security.Cryptography;
using System.Text.Json;

namespace DysonNetwork.Sphere.Auth;

public class AuthService(IConfiguration config, IHttpClientFactory httpClientFactory)
{
    public async Task<bool> ValidateCaptcha(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        
        var provider = config.GetSection("Captcha")["Provider"]?.ToLower();
        var apiSecret = config.GetSection("Captcha")["ApiSecret"];

        var client = httpClientFactory.CreateClient();
        
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        switch (provider)
        {
            case "cloudflare":
                var content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
                    content);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);

                return result?.Success == true;
            case "google":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
                response.EnsureSuccessStatusCode();

                json = await response.Content.ReadAsStringAsync();
                result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);

                return result?.Success == true;
            case "hcaptcha":
                content = new StringContent($"secret={apiSecret}&response={token}", System.Text.Encoding.UTF8,
                    "application/x-www-form-urlencoded");
                response = await client.PostAsync("https://hcaptcha.com/siteverify", content);
                response.EnsureSuccessStatusCode();

                json = await response.Content.ReadAsStringAsync();
                result = JsonSerializer.Deserialize<CaptchaVerificationResponse>(json, options: jsonOpts);

                return result?.Success == true;
            default:
                throw new ArgumentException("The server misconfigured for the captcha.");
        }
    }

    public string CreateToken(Session session)
    {
        // Load the private key for signing
        var privateKeyPem = File.ReadAllText(config["Jwt:PrivateKeyPath"]!);
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