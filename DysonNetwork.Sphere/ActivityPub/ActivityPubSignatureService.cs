using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.ActivityPub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubSignatureService(
    AppDatabase db,
    ActivityPubKeyService keyService,
    ILogger<ActivityPubSignatureService> logger,
    IConfiguration configuration
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    public bool VerifyIncomingRequest(HttpContext context, out string? actorUri)
    {
        actorUri = null;
        
        if (!context.Request.Headers.ContainsKey("Signature"))
        {
            logger.LogWarning("Request missing Signature header. Path: {Path}", context.Request.Path);
            return false;
        }
        
        var signatureHeader = context.Request.Headers["Signature"].ToString();
        logger.LogInformation("Incoming request with signature. Path: {Path}, SignatureHeader: {Signature}", 
            context.Request.Path, signatureHeader);
        
        var signatureParts = ParseSignatureHeader(signatureHeader);
        
        if (signatureParts == null)
        {
            logger.LogWarning("Invalid signature header format. SignatureHeader: {Signature}", signatureHeader);
            return false;
        }
        
        actorUri = signatureParts.GetValueOrDefault("keyId");
        if (string.IsNullOrEmpty(actorUri))
        {
            logger.LogWarning("No keyId in signature. SignatureParts: {Parts}", 
                string.Join(", ", signatureParts.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            return false;
        }
        
        logger.LogInformation("Verifying signature for actor: {ActorUri}", actorUri);
        
        var actor = GetActorByKeyId(actorUri);
        if (actor == null)
        {
            logger.LogWarning("Actor not found for keyId: {KeyId}", actorUri);
            return false;
        }
        
        if (string.IsNullOrEmpty(actor.PublicKey))
        {
            logger.LogWarning("Actor has no public key. ActorId: {ActorId}, Uri: {Uri}", actor.Id, actor.Uri);
            return false;
        }
        
        var signingString = BuildSigningString(context, signatureParts);
        var signature = signatureParts.GetValueOrDefault("signature");
        
        logger.LogInformation("Built signing string for verification. SigningString: {SigningString}, Signature: {Signature}", 
            signingString, signature?.Substring(0, Math.Min(50, signature?.Length ?? 0)) + "...");
        
        if (string.IsNullOrEmpty(signingString) || string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Failed to build signing string or extract signature");
            return false;
        }
        
        var isValid = keyService.Verify(actor.PublicKey, signingString, signature);
        
        if (!isValid)
        {
            logger.LogError("Signature verification failed for actor: {ActorUri}. SigningString: {SigningString}", 
                actorUri, signingString);
        }
        else
        {
            logger.LogInformation("Signature verified successfully for actor: {ActorUri}", actorUri);
        }
        
        return isValid;
    }

    public async Task<Dictionary<string, string>> SignOutgoingRequest(
        HttpRequestMessage request,
        string actorUri
    )
    {
        var publisher = await GetPublisherByActorUri(actorUri);
        if (publisher == null)
            throw new InvalidOperationException("Publisher not found");
        
        var keyPair = await GetOrGenerateKeyPairAsync(publisher);
        var keyId = $"{actorUri}#main-key";
        
        logger.LogInformation("Signing outgoing request. ActorUri: {ActorUri}, PublisherId: {PublisherId}", 
            actorUri, publisher.Id);
        
        var headersToSign = new[] { "(request-target)", "host", "date", "digest", "content-type" };
        var signingString = BuildSigningStringForRequest(request, headersToSign);
        
        logger.LogInformation("Signing string for outgoing request: {SigningString}", signingString);
        
        var signature = keyService.Sign(keyPair.privateKeyPem, signingString);
        
        logger.LogInformation("Generated signature: {Signature}", signature.Substring(0, Math.Min(50, signature.Length)) + "...");
        
        return new Dictionary<string, string>
        {
            ["keyId"] = keyId,
            ["algorithm"] = "rsa-sha256",
            ["headers"] = string.Join(" ", headersToSign),
            ["signature"] = signature
        };
    }

    private SnFediverseActor? GetActorByKeyId(string keyId)
    {
        var actorUri = keyId.Split('#')[0];
        return db.FediverseActors.FirstOrDefault(a => a.Uri == actorUri);
    }

    private async Task<SnPublisher?> GetPublisherByActorUri(string actorUri)
    {
        var username = actorUri.Split('/')[^1];
        return await db.Publishers.FirstOrDefaultAsync(p => p.Name == username);
    }

    private async Task<(string privateKeyPem, string publicKeyPem)> GetOrGenerateKeyPairAsync(SnPublisher publisher)
    {
        var privateKeyPem = GetPublisherKey(publisher, "private_key");
        var publicKeyPem = GetPublisherKey(publisher, "public_key");
        
        if (string.IsNullOrEmpty(privateKeyPem) || string.IsNullOrEmpty(publicKeyPem))
        {
            logger.LogInformation("Generating new key pair for publisher: {PublisherId} ({Name})", 
                publisher.Id, publisher.Name);
            
            var (newPrivate, newPublic) = keyService.GenerateKeyPair();
            SavePublisherKey(publisher, "private_key", newPrivate);
            SavePublisherKey(publisher, "public_key", newPublic);
            
            publisher.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(publisher);
            await db.SaveChangesAsync();
            
            logger.LogInformation("Saved new key pair to database for publisher: {PublisherId}", publisher.Id);
            
            return (newPrivate, newPublic);
        }
        
        logger.LogInformation("Using existing key pair for publisher: {PublisherId}", publisher.Id);
        return (privateKeyPem, publicKeyPem);
    }

    private string? GetPublisherKey(SnPublisher publisher, string keyName)
    {
        if (publisher.Meta == null)
            return null;
        
        var metadata = publisher.Meta as Dictionary<string, object>;
        return metadata?.GetValueOrDefault(keyName)?.ToString();
    }

    private void SavePublisherKey(SnPublisher publisher, string keyName, string keyValue)
    {
        publisher.Meta ??= new Dictionary<string, object>();
        var metadata = publisher.Meta as Dictionary<string, object>;
        if (metadata != null)
        {
            metadata[keyName] = keyValue;
        }
    }

    private Dictionary<string, string>? ParseSignatureHeader(string signatureHeader)
    {
        var parts = new Dictionary<string, string>();
        
        foreach (var item in signatureHeader.Split(','))
        {
            var keyValue = item.Trim().Split('=', 2);
            if (keyValue.Length != 2)
                continue;
            
            var key = keyValue[0];
            var value = keyValue[1].Trim('"');
            parts[key] = value;
        }
        
        return parts;
    }

    private string BuildSigningString(HttpContext context, Dictionary<string, string> signatureParts)
    {
        var headers = signatureParts.GetValueOrDefault("headers")?.Split(' ');
        if (headers == null || headers.Length == 0)
            return string.Empty;
        
        var sb = new StringBuilder();
        
        foreach (var header in headers)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            
            sb.Append(header.ToLower());
            sb.Append(": ");
            
            if (header == "(request-target)")
            {
                var method = context.Request.Method.ToLower();
                var path = context.Request.Path.Value ?? "";
                sb.Append($"{method} {path}");
            }
            else
            {
                if (context.Request.Headers.TryGetValue(header, out var values))
                {
                    sb.Append(values.ToString());
                }
            }
        }
        
        return sb.ToString();
    }

    private string BuildSigningStringForRequest(HttpRequestMessage request, string[] headers)
    {
        var sb = new StringBuilder();
        logger.LogInformation("Building signing string for request. Headers to sign: {Headers}", 
            string.Join(", ", headers));
        logger.LogInformation("Request details: Method={Method}, Uri={Uri}", 
            request.Method, request.RequestUri);
        
        foreach (var header in headers)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            
            sb.Append(header.ToLower());
            sb.Append(": ");
            
            if (header == "(request-target)")
            {
                var method = request.Method.Method.ToLower();
                var path = request.RequestUri?.PathAndQuery ?? "/";
                sb.Append($"{method} {path}");
                logger.LogInformation("  (request-target): {Value}", $"{method} {path}");
            }
            else if (header == "host")
            {
                if (request.Headers.Contains("Host"))
                {
                    var value = request.Headers.GetValues("Host").First();
                    sb.Append(value);
                    logger.LogInformation("  host: {Value}", value);
                }
                else
                {
                    logger.LogWarning("Host header not found in request");
                }
            }
            else if (header == "date")
            {
                if (request.Headers.Contains("Date"))
                {
                    var value = request.Headers.GetValues("Date").First();
                    sb.Append(value);
                    logger.LogInformation("  date: {Value}", value);
                }
                else
                {
                    logger.LogWarning("Date header not found in request");
                }
            }
            else if (header == "digest")
            {
                if (request.Headers.Contains("Digest"))
                {
                    var value = request.Headers.GetValues("Digest").First();
                    sb.Append(value);
                    logger.LogInformation("  digest: {Value}", value);
                }
                else
                {
                    logger.LogWarning("Digest header not found in request");
                }
            }
            else if (header == "content-type")
            {
                if (request.Content?.Headers.Contains("Content-Type") == true)
                {
                    var value = request.Content.Headers.GetValues("Content-Type").First();
                    sb.Append(value);
                    logger.LogInformation("  content-type: {Value}", value);
                }
                else
                {
                    logger.LogWarning("Content-Type header not found in request");
                }
            }
        }
        
        return sb.ToString();
    }
}
