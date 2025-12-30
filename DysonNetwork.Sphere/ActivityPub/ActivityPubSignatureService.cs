using System.Text;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubSignatureService(
    AppDatabase db,
    ActivityPubKeyService keyService,
    ActivityPubDiscoveryService discoveryService,
    ICacheService cache,
    ILogger<ActivityPubSignatureService> logger,
    IConfiguration configuration
)
{
    private const string RequestTarget = "(request-target)";
    private const string PublicKeyCachePrefix = "ap:publickey:";
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    public bool VerifyIncomingRequest(HttpContext context, out string? actorUri)
    {
        actorUri = null;
        
        if (!context.Request.Headers.TryGetValue("Signature", out var value))
        {
            logger.LogWarning("Request missing Signature header. Path: {Path}", context.Request.Path);
            return false;
        }
        
        var signatureHeader = value.ToString();
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
        
        var publicKey = GetOrFetchPublicKeyAsync(actorUri).GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(publicKey))
        {
            logger.LogWarning("Could not fetch public key for actor: {ActorUri}", actorUri);
            return false;
        }
        
        var signingString = BuildIncomingSigningString(context, signatureParts);
        var signature = signatureParts.GetValueOrDefault("signature");
        
        logger.LogInformation("Built signing string for verification. SigningString: {SigningString}, Signature: {Signature}", 
            signingString, signature?.Substring(0, Math.Min(50, signature?.Length ?? 0)) + "...");
        
        if (string.IsNullOrEmpty(signingString) || string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Failed to build signing string or extract signature");
            return false;
        }
        
        var isValid = keyService.Verify(publicKey, signingString, signature);
        
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
        
        var headersToSign = new[] { RequestTarget, "host", "date", "digest" };
        var signingString = BuildOutgoingSigningString(request, headersToSign);
        
        logger.LogInformation("Signing string for outgoing request: {SigningString}", signingString);
        
        var signature = ActivityPubKeyService.Sign(keyPair.privateKeyPem, signingString);
        
        logger.LogInformation("Generated signature: {Signature}", signature.Substring(0, Math.Min(50, signature.Length)) + "...");
        
        return new Dictionary<string, string>
        {
            ["keyId"] = keyId,
            ["algorithm"] = "rsa-sha256",
            ["headers"] = string.Join(" ", headersToSign),
            ["signature"] = signature
        };
    }

    private async Task<string?> GetOrFetchPublicKeyAsync(string keyId)
    {
        var actorUri = keyId.Split('#')[0];
        var cacheKey = $"{PublicKeyCachePrefix}{actorUri}";
        
        var cachedKey = await cache.GetAsync<string>(cacheKey);
        if (!string.IsNullOrEmpty(cachedKey))
        {
            logger.LogInformation("Using cached public key for actor: {ActorUri}", actorUri);
            return cachedKey;
        }
        
        var actor = db.FediverseActors.FirstOrDefault(a => a.Uri == actorUri);
        
        if (actor == null)
        {
            var instance = await db.FediverseInstances.FirstOrDefaultAsync(i => i.Domain == new Uri(actorUri).Host);
            if (instance == null)
            {
                instance = new SnFediverseInstance
                {
                    Domain = new Uri(actorUri).Host,
                    Name = new Uri(actorUri).Host
                };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }
            
            actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = actorUri.Split('/').Last(),
                DisplayName = actorUri.Split('/').Last(),
                InstanceId = instance.Id
            };
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
            
            await discoveryService.FetchActorDataAsync(actor);
        }
        else if (string.IsNullOrEmpty(actor.PublicKey))
        {
            await discoveryService.FetchActorDataAsync(actor);
        }
        
        if (string.IsNullOrEmpty(actor.PublicKey))
        {
            logger.LogWarning("Still no public key after fetch for actor: {ActorUri}", actorUri);
            return null;
        }
        
        await cache.SetAsync(cacheKey, actor.PublicKey, TimeSpan.FromHours(24));
        logger.LogInformation("Cached public key for actor: {ActorUri}", actorUri);
        
        return actor.PublicKey;
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
        return keyName switch
        {
            "private_key" => publisher.PrivateKeyPem,
            "public_key" => publisher.PublicKeyPem,
            _ => null
        };
    }

    private void SavePublisherKey(SnPublisher publisher, string keyName, string keyValue)
    {
        switch (keyName)
        {
            case "private_key":
                publisher.PrivateKeyPem = keyValue;
                break;
            case "public_key":
                publisher.PublicKeyPem = keyValue;
                break;
        }
    }

    private static Dictionary<string, string>? ParseSignatureHeader(string signatureHeader)
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

    private string BuildIncomingSigningString(HttpContext context, Dictionary<string, string> signatureParts)
    {
        var headers = signatureParts.GetValueOrDefault("headers")?.Split(' ');
        if (headers == null || headers.Length == 0)
            return string.Empty;
        
        var sb = new StringBuilder();
        
        foreach (var header in headers)
        {
            if (header == "content-type")
                continue;
            
            if (sb.Length > 0)
                sb.Append('\n');
            
            sb.Append(header.ToLower());
            sb.Append(": ");
            
            switch (header)
            {
                case RequestTarget:
                {
                    var method = context.Request.Method.ToLower();
                    var path = context.Request.Path.Value ?? "";
                    sb.Append($"{method} {path}");
                    break;
                }
                case "host":
                    sb.Append(Domain);
                    break;
                default:
                {
                    if (context.Request.Headers.TryGetValue(header, out var values))
                    {
                        sb.Append(values.ToString());
                    }

                    break;
                }
            }
        }
        
        return sb.ToString();
    }

    private string BuildOutgoingSigningString(HttpRequestMessage request, string[] headers)
    {
        var sb = new StringBuilder();
        logger.LogInformation("Building signing string for request. Headers to sign: {Headers}", 
            string.Join(", ", headers));
        logger.LogInformation("Request details: Method={Method}, Uri={Uri}", 
            request.Method, request.RequestUri);
        
        foreach (var header in headers)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            
            sb.Append(header.ToLower());
            sb.Append(": ");
            
            switch (header)
            {
                case RequestTarget:
                {
                    var method = request.Method.Method.ToLower();
                    var path = request.RequestUri?.PathAndQuery ?? "/";
                    sb.Append($"{method} {path}");
                    logger.LogInformation("  {Key}: {Value}", RequestTarget, $"{method} {path}");
                    break;
                }
                case "host" when request.Headers.Contains("Host"):
                {
                    var value = request.Headers.GetValues("Host").First();
                    sb.Append(value);
                    logger.LogInformation("  host: {Value}", value);
                    break;
                }
                case "host":
                    logger.LogWarning("Host header not found in request");
                    break;
                case "date" when request.Headers.Contains("Date"):
                {
                    var value = request.Headers.GetValues("Date").First();
                    sb.Append(value);
                    logger.LogInformation("  date: {Value}", value);
                    break;
                }
                case "date":
                    logger.LogWarning("Date header not found in request");
                    break;
                case "digest" when request.Headers.Contains("Digest"):
                {
                    var value = request.Headers.GetValues("Digest").First();
                    sb.Append(value);
                    logger.LogInformation("  digest: {Value}", value);
                    break;
                }
                case "digest":
                    logger.LogWarning("Digest header not found in request");
                    break;
            }
        }
        
        return sb.ToString();
    }
}
