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
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    public static async Task<SignatureVerificationResult> VerifyAsync(
        HttpContext context,
        string? algorithm = null,
        IEnumerable<string>? requiredHeaders = null
    )
    {
        var result = new SignatureVerificationResult();

        if (!context.Request.Headers.TryGetValue("Signature", out var signatureHeader))
        {
            result.Error = "Missing Signature header";
            return result;
        }

        HttpSignatureHeader? signature = null;
        try
        {
            signature = HttpSignature.Parse(signatureHeader.ToString());
        }
        catch (HttpSignatureException ex)
        {
            result.Error = $"Invalid signature format: {ex.Message}";
            return result;
        }

        var keyId = signature.KeyId.Split('#')[0];
        result.KeyId = signature.KeyId;

        var actualAlgorithm = algorithm ?? KeyAlgorithm.GetActualAlgorithm(signature.Algorithm);
        result.ActualAlgorithm = actualAlgorithm;

        var headersToVerify = requiredHeaders?.ToList() ?? GetDefaultHeaders(context.Request.Method);

        try
        {
            if (!headersToVerify.All(h => signature.Headers.Contains(h.ToLowerInvariant())))
            {
                var missing = headersToVerify.Where(h => !signature.Headers.Contains(h.ToLowerInvariant())).ToList();
                result.Error = $"Missing required headers: {string.Join(", ", missing)}";
                return result;
            }

            if (!VerifyTimestamp(context, signature))
            {
                result.Error = "Timestamp out of range";
                return result;
            }

            var bodyHash = await VerifyDigestAsync(context);
            if (bodyHash != null && !signature.Headers.Contains("digest"))
            {
                result.Error = "Digest header required for request with body";
                return result;
            }

            result.ActorUri = keyId;
            result.IsValid = true;

            return result;
        }
        catch (HttpSignatureException ex)
        {
            result.Error = ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"Verification error: {ex.Message}";
            return result;
        }
    }

    public static async Task<bool> VerifyAsync(
        HttpContext context,
        HttpSignatureHeader signature,
        IEnumerable<string>? requiredHeaders = null,
        string? keyPem = null
    )
    {
        requiredHeaders ??= new[] { "(request-target)", "host", "date" };
        var requiredHeadersList = requiredHeaders.ToList();

        if (!requiredHeadersList.All(h => signature.Headers.Contains(h.ToLowerInvariant())))
        {
            throw new HttpSignatureException("Request is missing required headers");
        }

        if (!VerifyTimestamp(context, signature))
        {
            throw new HttpSignatureException("Timestamp out of range");
        }

        var bodyLength = context.Request.ContentLength ?? 0;
        if (bodyLength > 0 && signature.Headers.Contains("digest"))
        {
            await VerifyDigestAsync(context);
        }

        if (string.IsNullOrEmpty(keyPem))
        {
            throw new HttpSignatureException("Key PEM is required for verification");
        }

        var dateHeader = context.Request.Headers.Date.FirstOrDefault();
        var digestHeader = context.Request.Headers.TryGetValue("digest", out var digestValues) ? digestValues.FirstOrDefault() : null;
        var contentTypeHeader = context.Request.Headers.TryGetValue("Content-Type", out var ctValues) ? ctValues.FirstOrDefault() : null;

        var signingString = GenerateSigningString(
            signature.Headers,
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            context.Request.Host.Host,
            signature.Created,
            signature.Expires,
            dateHeader,
            digestHeader,
            contentTypeHeader
        );

        return await VerifySignatureAsync(keyPem, signingString, signature.Signature);
    }

    private static bool VerifyTimestamp(HttpContext context, HttpSignatureHeader signature)
    {
        var createdValue = signature.Created;
        var expiresValue = signature.Expires;
        var dateHeader = context.Request.Headers.Date.FirstOrDefault();

        if (!string.IsNullOrEmpty(expiresValue) && long.TryParse(expiresValue, out var expiresUnix))
        {
            var expiresTime = DateTime.UnixEpoch.AddSeconds(expiresUnix);
            if (DateTime.UtcNow > expiresTime)
            {
                throw new HttpSignatureException("Request signature is expired");
            }
        }

        if (!string.IsNullOrEmpty(createdValue) && long.TryParse(createdValue, out var createdUnix))
        {
            var createdTime = DateTime.UnixEpoch.AddSeconds(createdUnix);
            var skew = DateTime.UtcNow - createdTime;

            if (skew > MaxClockSkew)
            {
                throw new HttpSignatureException("Request signature is too old");
            }

            if (skew < -MaxFutureSkew)
            {
                throw new HttpSignatureException("Request signature is from the future");
            }

            return true;
        }

        if (!string.IsNullOrEmpty(dateHeader) && DateTime.TryParse(dateHeader, out var dateTime))
        {
            var skew = DateTime.UtcNow - dateTime.ToUniversalTime();

            if (skew > MaxClockSkew)
            {
                throw new HttpSignatureException("Request date is too old");
            }

            if (skew < -MaxFutureSkew)
            {
                throw new HttpSignatureException("Request date is from the future");
            }

            return true;
        }

        throw new HttpSignatureException("Neither date nor (created) are present");
    }

    private static async Task<string?> VerifyDigestAsync(HttpContext context)
    {
        var bodyLength = context.Request.ContentLength ?? 0;
        if (bodyLength <= 0)
            return null;

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        if (context.Request.Headers.TryGetValue("digest", out var digestValues))
        {
            var expectedDigest = digestValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(expectedDigest))
            {
                var expectedHash = ExtractDigestHash(expectedDigest, "SHA-256");
                var actualDigest = Convert.ToBase64String(hash);

                if (expectedHash != null && expectedHash != actualDigest)
                {
                    throw new HttpSignatureException("Digest mismatch");
                }

                return actualDigest;
            }
        }

        return Convert.ToBase64String(hash);
    }

    private static string? ExtractDigestHash(string digestHeader, string algorithm)
    {
        var prefix = $"{algorithm}=".ToUpperInvariant();
        var headerUpper = digestHeader.ToUpperInvariant();

        if (headerUpper.StartsWith(prefix))
        {
            return digestHeader[prefix.Length..];
        }

        if (headerUpper.StartsWith(algorithm.ToUpperInvariant() + "="))
        {
            var parts = digestHeader.Split('=', 2);
            return parts.Length > 1 ? parts[1] : null;
        }

        return null;
    }

    public static async Task<bool> VerifySignatureAsync(string keyPem, string signingString, byte[] signatureBytes, string algorithm = KeyAlgorithm.RSA_SHA256)
    {
        return await Task.Run(() =>
        {
            using var rsa = RSA.Create();
            ImportKey(rsa, keyPem, false);

            var hashAlgorithm = algorithm switch
            {
                KeyAlgorithm.RSA_SHA512 => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };

            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(signingString),
                signatureBytes,
                hashAlgorithm,
                RSASignaturePadding.Pkcs1
            );
        });
    }

    public static HttpSignatureHeader Parse(string header)
    {
        var parts = header.Split(',')
            .Select(s => s.Split('=', 2))
            .ToDictionary(
                p => p[0].Trim().ToLowerInvariant(),
                p => p.Length > 1 ? p[1].Trim('"').Trim() : string.Empty
            );

        if (!parts.TryGetValue("signature", out var signatureB64) || string.IsNullOrEmpty(signatureB64))
        {
            throw new HttpSignatureException("Signature string is missing the signature field");
        }

        if (!parts.TryGetValue("headers", out var headersStr) || string.IsNullOrEmpty(headersStr))
        {
            throw new HttpSignatureException("Signature string is missing the headers field");
        }

        if (!parts.TryGetValue("keyid", out var keyId) || string.IsNullOrEmpty(keyId))
        {
            throw new HttpSignatureException("Signature string is missing the keyId field");
        }

        var algorithm = parts.TryGetValue("algorithm", out var algo) && !string.IsNullOrEmpty(algo)
            ? algo
            : "rsa-sha256";

        var created = parts.GetValueOrDefault("created");
        var expires = parts.GetValueOrDefault("expires");
        var opaque = parts.GetValueOrDefault("opaque");

        var signature = Convert.FromBase64String(signatureB64);
        var headers = headersStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.ToLowerInvariant())
            .ToArray();

        return new HttpSignatureHeader(keyId, algorithm, signature, headers, created, expires, opaque);
    }

    public static string GenerateSigningString(
        IEnumerable<string> headers,
        string requestMethod,
        string requestPath,
        string? requestQuery,
        string? host = null,
        string? created = null,
        string? expires = null,
        string? date = null,
        string? digest = null,
        string? contentType = null
    )
    {
        var sb = new StringBuilder();
        var headersList = headers.ToList();

        for (var i = 0; i < headersList.Count; i++)
        {
            var header = headersList[i].ToLowerInvariant();
            sb.Append($"{header}: ");
            sb.Append(header switch
            {
                "(request-target)" => $"{requestMethod.ToLowerInvariant()} {requestPath}{requestQuery ?? ""}",
                "host" => host ?? "",
                "(created)" => created ?? throw new HttpSignatureException("Signature is missing created param"),
                "(expires)" => expires ?? throw new HttpSignatureException("Signature is missing expires param"),
                "date" => date ?? throw new HttpSignatureException("Signature is missing date header"),
                "digest" => digest ?? "",
                "content-type" => contentType ?? "",
                _ => ""
            });

            if (i < headersList.Count - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    public static SigningResult CreateSigningString(
        string requestMethod,
        string requestPath,
        string? requestQuery,
        string? host = null,
        bool includeDigest = true,
        bool useCreatedHeader = true
    )
    {
        var result = new SigningResult();

        var headers = new List<string> { "(request-target)", "host", "date" };

        if (includeDigest)
        {
            headers.Add("digest");
        }

        string? created = null;
        if (useCreatedHeader)
        {
            headers.Add("(created)");
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }

        var date = DateTime.UtcNow.ToString("r");

        result.SigningString = GenerateSigningString(
            headers,
            requestMethod,
            requestPath,
            requestQuery,
            host,
            created,
            null,
            date
        );
        result.Headers = headers;
        result.Created = created;

        return result;
    }

    public static async Task SignRequestAsync(
        HttpRequestMessage request,
        string privateKeyPem,
        string keyId,
        string algorithm = KeyAlgorithm.HS2019,
        bool includeDigest = true,
        bool useCreatedHeader = true
    )
    {
        request.Headers.Date = DateTimeOffset.UtcNow;

        var hostHeader = request.RequestUri?.Host;
        if (!string.IsNullOrEmpty(hostHeader))
        {
            request.Headers.Host = hostHeader;
        }

        string? digestHash = null;
        if (includeDigest && request.Content != null)
        {
            var content = await request.Content!.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(content))
            {
                digestHash = ComputeDigest(content);
                request.Headers.Remove("Digest");
                request.Headers.Add("Digest", $"SHA-256={digestHash}");
            }
        }

        var headers = new List<string> { "(request-target)", "host", "date" };
        if (includeDigest)
        {
            headers.Add("digest");
        }

        string? created = null;
        if (useCreatedHeader)
        {
            headers.Add("(created)");
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }

        var date = DateTime.UtcNow.ToString("r");

        var signingString = GenerateSigningString(
            headers,
            request.Method.Method,
            request.RequestUri?.AbsolutePath ?? "/",
            request.RequestUri?.Query,
            hostHeader,
            created,
            null,
            date
        );

        var actualAlgorithm = algorithm == KeyAlgorithm.HS2019 ? KeyAlgorithm.RSA_SHA256 : algorithm;

        var rsa = RSA.Create();
        ImportKey(rsa, privateKeyPem, true);

        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(signingString),
            actualAlgorithm == KeyAlgorithm.RSA_SHA512 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        var signatureHeader = $"keyId=\"{keyId}\",algorithm=\"{algorithm.ToLowerInvariant()}\",headers=\"{string.Join(" ", headers)}\",signature=\"{signatureBase64}\"";

        request.Headers.Remove("Signature");
        request.Headers.Add("Signature", signatureHeader);
    }

    public static string ComputeDigest(string content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
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

    public static (string privateKeyPem, string publicKeyPem) GenerateKeyPair(int keySize = 2048)
    {
        using var rsa = RSA.Create(keySize);

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

    private static List<string> GetDefaultHeaders(string method)
    {
        var headers = new List<string> { "(request-target)", "host", "date" };

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
        {
            headers.Add("digest");
        }

        return headers;
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
);

public class HttpSignatureException : Exception
{
    public HttpSignatureException(string message) : base(message) { }
}

public class SignatureVerificationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public string? KeyId { get; set; }
    public string? ActorUri { get; set; }
    public string? ActualAlgorithm { get; set; }
}

public class SigningResult
{
    public string? SigningString { get; set; }
    public List<string>? Headers { get; set; }
    public string? Created { get; set; }
}