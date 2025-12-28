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
            return false;
        
        var signatureHeader = context.Request.Headers["Signature"].ToString();
        var signatureParts = ParseSignatureHeader(signatureHeader);
        
        if (signatureParts == null)
        {
            logger.LogWarning("Invalid signature header format");
            return false;
        }
        
        actorUri = signatureParts.GetValueOrDefault("keyId");
        if (string.IsNullOrEmpty(actorUri))
        {
            logger.LogWarning("No keyId in signature");
            return false;
        }
        
        var actor = GetActorByKeyId(actorUri);
        if (actor == null)
        {
            logger.LogWarning("Actor not found for keyId: {KeyId}", actorUri);
            return false;
        }
        
        if (string.IsNullOrEmpty(actor.PublicKey))
        {
            logger.LogWarning("Actor has no public key: {ActorId}", actor.Id);
            return false;
        }
        
        var signingString = BuildSigningString(context, signatureParts);
        var signature = signatureParts.GetValueOrDefault("signature");
        
        if (string.IsNullOrEmpty(signingString) || string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Failed to build signing string or extract signature");
            return false;
        }
        
        var isValid = keyService.Verify(actor.PublicKey, signingString, signature);
        
        if (!isValid)
            logger.LogWarning("Signature verification failed for actor: {ActorUri}", actorUri);
        
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
        
        var keyPair = GetOrGenerateKeyPair(publisher);
        var keyId = $"{actorUri}#main-key";
        
        var headersToSign = new[] { "(request-target)", "host", "date" };
        var signingString = BuildSigningStringForRequest(request, headersToSign);
        var signature = keyService.Sign(keyPair.privateKeyPem, signingString);
        
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

    private (string? privateKeyPem, string? publicKeyPem) GetOrGenerateKeyPair(SnPublisher publisher)
    {
        var privateKeyPem = GetPublisherKey(publisher, "private_key");
        var publicKeyPem = GetPublisherKey(publisher, "public_key");
        
        if (string.IsNullOrEmpty(privateKeyPem) || string.IsNullOrEmpty(publicKeyPem))
        {
            var (newPrivate, newPublic) = keyService.GenerateKeyPair();
            SavePublisherKey(publisher, "private_key", newPrivate);
            SavePublisherKey(publisher, "public_key", newPublic);
            return (newPrivate, newPublic);
        }
        
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
            }
            else if (header == "host")
            {
                sb.Append(request.RequestUri?.Host);
            }
            else if (header == "date")
            {
                if (request.Headers.Contains("Date"))
                {
                    sb.Append(request.Headers.GetValues("Date").First());
                }
            }
        }
        
        return sb.ToString();
    }
}
