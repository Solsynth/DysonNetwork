using System.Net;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public static class HttpSignature
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static async Task<bool> VerifyAsync(
        HttpContext context,
        HttpSignatureHeader signature,
        IEnumerable<string>? requiredHeaders = null,
        string? keyPem = null
    )
    {
        requiredHeaders ??= new[] { "(request-target)", "host", "date" };
        var requiredHeadersList = requiredHeaders.ToList();

        if (!requiredHeadersList.All(h => signature.Headers.Contains(h)))
        {
            throw new HttpSignatureException("Request is missing required headers");
        }

        var dateHeader = context.Request.Headers.Date.FirstOrDefault();
        var createdValue = signature.Created;

        if (createdValue == null && string.IsNullOrEmpty(dateHeader))
        {
            throw new HttpSignatureException("Neither date nor (created) are present");
        }

        if (createdValue != null)
        {
            if (long.TryParse(createdValue, out var createdUnix))
            {
                var createdTime = DateTime.UnixEpoch.AddSeconds(createdUnix);
                if (DateTime.UtcNow - createdTime > MaxClockSkew)
                {
                    throw new HttpSignatureException("Request signature is too old");
                }
            }
        }
        else if (!string.IsNullOrEmpty(dateHeader))
        {
            if (DateTime.TryParse(dateHeader, out var dateTime))
            {
                if (DateTime.Now - dateTime > MaxClockSkew)
                {
                    throw new HttpSignatureException("Request signature is too old");
                }
            }
        }

        if (signature.Expires != null)
        {
            if (long.TryParse(signature.Expires, out var expiresUnix))
            {
                var expiresTime = DateTime.UnixEpoch.AddSeconds(expiresUnix);
                if (DateTime.UtcNow > expiresTime)
                {
                    throw new HttpSignatureException("Request signature is expired");
                }
            }
        }

        var bodyLength = context.Request.ContentLength ?? 0;
        if (bodyLength > 0 && signature.Headers.Contains("digest"))
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;
            
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(context.Request.Body);
            context.Request.Body.Position = 0;

            var expectedDigest = context.Request.Headers.TryGetValue("digest", out var digestValues) 
                ? digestValues.FirstOrDefault() 
                : null;
                
            if (!string.IsNullOrEmpty(expectedDigest))
            {
                var expectedHash = expectedDigest.StartsWith("SHA-256=", StringComparison.OrdinalIgnoreCase)
                    ? expectedDigest[8..]
                    : null;
                
                var actualDigest = Convert.ToBase64String(hash);
                
                if (expectedHash != null && expectedHash != actualDigest)
                {
                    throw new HttpSignatureException("Digest mismatch");
                }
            }
        }

        if (string.IsNullOrEmpty(keyPem))
        {
            throw new HttpSignatureException("Key PEM is required for verification");
        }

        var signingString = GenerateSigningString(
            signature.Headers,
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            context.Request.Host.Host
        );

        return await VerifySignatureAsync(keyPem, signingString, signature.Signature);
    }

    public static async Task<bool> VerifySignatureAsync(string keyPem, string signingString, byte[] signatureBytes)
    {
        return await Task.Run(() =>
        {
            using var rsa = RSA.Create();
            ImportKey(rsa, keyPem, false);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(signingString),
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        });
    }

    public static HttpSignatureHeader Parse(string header)
    {
        var parts = header.Split(',')
            .Select(s => s.Split('=', 2))
            .ToDictionary(
                p => p[0].Trim(),
                p => p.Length > 1 ? p[1].Trim('"') : string.Empty
            );

        if (!parts.TryGetValue("signature", out var signatureB64) || string.IsNullOrEmpty(signatureB64))
        {
            throw new HttpSignatureException("Signature string is missing the signature field");
        }

        if (!parts.TryGetValue("headers", out var headersStr) || string.IsNullOrEmpty(headersStr))
        {
            throw new HttpSignatureException("Signature string is missing the headers field");
        }

        if (!parts.TryGetValue("keyId", out var keyId) || string.IsNullOrEmpty(keyId))
        {
            throw new HttpSignatureException("Signature string is missing the keyId field");
        }

        var algorithm = "rsa-sha256";
        if (parts.TryGetValue("algorithm", out var algo) && !string.IsNullOrEmpty(algo))
        {
            algorithm = algo;
        }

        parts.TryGetValue("created", out var created);
        parts.TryGetValue("expires", out var expires);
        parts.TryGetValue("opaque", out var opaque);

        var signature = Convert.FromBase64String(signatureB64);
        var headers = headersStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new HttpSignatureHeader(keyId, algorithm, signature, headers, created, expires, opaque);
    }

    public static string GenerateSigningString(
        IEnumerable<string> headers,
        string requestMethod,
        string requestPath,
        string? requestQuery,
        string? host = null
    )
    {
        var sb = new StringBuilder();
        var headersList = headers.ToList();

        for (var i = 0; i < headersList.Count; i++)
        {
            var header = headersList[i];
            sb.Append($"{header.ToLowerInvariant()}: ");
            sb.Append(header.ToLowerInvariant() switch
            {
                "(request-target)" => $"{requestMethod.ToLowerInvariant()} {requestPath}{requestQuery ?? ""}",
                "host" => host ?? "",
                "(created)" => throw new HttpSignatureException("Signature is missing created param"),
                "(expires)" => throw new HttpSignatureException("Signature is missing expires param"),
                _ => ""
            });

            if (i < headersList.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static async Task SignRequestAsync(
        HttpRequestMessage request,
        string keyPem,
        string keyId,
        IEnumerable<string>? requiredHeaders = null
    )
    {
        requiredHeaders ??= new[] { "(request-target)", "host", "date" };
        var headersList = requiredHeaders.ToList();

        request.Headers.Date = DateTimeOffset.UtcNow;
        
        var hostHeader = request.RequestUri?.Host;
        if (!string.IsNullOrEmpty(hostHeader))
        {
            request.Headers.Host = hostHeader;
        }

        if (!request.Headers.Contains("date") && headersList.Contains("date"))
        {
            request.Headers.Date = DateTimeOffset.UtcNow;
        }

        if (request.Content != null && !request.Headers.Contains("digest"))
        {
            var content = await request.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(content))
            {
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                var digest = $"SHA-256={Convert.ToBase64String(hash)}";
                request.Headers.Add("Digest", digest);
            }
        }

        var signingString = GenerateSigningString(
            headersList,
            request.Method.Method,
            request.RequestUri?.AbsolutePath ?? "/",
            request.RequestUri?.Query,
            hostHeader
        );

        var rsa = RSA.Create();
        ImportKey(rsa, keyPem, true);

        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(signingString),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        var signatureBase64 = Convert.ToBase64String(signatureBytes);
        var signatureHeader = $"keyId=\"{keyId}\",headers=\"{string.Join(" ", headersList)}\",algorithm=\"rsa-sha256\",signature=\"{signatureBase64}\"";

        request.Headers.Remove("Signature");
        request.Headers.Add("Signature", signatureHeader);
    }

    private static void ImportKey(RSA rsa, string keyPem, bool isPrivate)
    {
        var lines = keyPem.Split('\n')
            .Where(line => !line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var keyBytes = Convert.FromBase64String(string.Join("", lines));

        if (isPrivate)
        {
            rsa.ImportRSAPrivateKey(keyBytes, out _);
        }
        else
        {
            if (keyPem.Contains("-----BEGIN RSA PUBLIC KEY-----"))
            {
                rsa.ImportRSAPublicKey(keyBytes, out _);
            }
            else
            {
                rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }
        }
    }

    public static (string privateKeyPem, string publicKeyPem) GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);

        var privateKey = rsa.ExportRSAPrivateKey();
        var publicKey = rsa.ExportSubjectPublicKeyInfo();

        var privateKeyPem = ConvertToPem(privateKey, "RSA PRIVATE KEY");
        var publicKeyPem = ConvertToPem(publicKey, "PUBLIC KEY");

        return (privateKeyPem, publicKeyPem);
    }

    private static string ConvertToPem(byte[] keyData, string keyType)
    {
        var sb = new StringBuilder();
        sb.Append($"-----BEGIN {keyType}-----\n");
        sb.Append(Convert.ToBase64String(keyData) + "\n");
        sb.Append($"-----END {keyType}-----");
        return sb.ToString();
    }
}

public record HttpSignatureHeader(
    string KeyId,
    string Algorithm,
    byte[] Signature,
    IEnumerable<string> Headers,
    string? Created,
    string? Expires,
    string? Opaque
)
{
    public IEnumerable<string> Headers { get; } = Headers;

    public string? Created => Created;

    public string? Expires => Expires;
}

public class HttpSignatureException : Exception
{
    public HttpSignatureException(string message) : base(message) { }
}