using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Security.Cryptography;
using System.Text;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubSignatureService(
    AppDatabase db,
    ActivityPubKeyService keyService,
    ActivityPubDiscoveryService discoveryService,
    ILogger<ActivityPubSignatureService> logger,
    IConfiguration configuration
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    public async Task<(bool isValid, string? actorUri)> VerifyIncomingRequestAsync(HttpContext context)
    {
        var actorUri = (string?)null;
        
        if (!context.Request.Headers.TryGetValue("Signature", out var signatureHeader))
        {
            logger.LogWarning("Request missing Signature header. Path: {Path}", context.Request.Path);
            return (false, null);
        }

        var headerValue = signatureHeader.ToString();
        logger.LogDebug("Verifying signature. Path: {Path}", context.Request.Path);
        
        HttpSignatureHeader signature;
        try
        {
            signature = HttpSignature.Parse(headerValue);
        }
        catch (HttpSignatureException ex)
        {
            logger.LogWarning("Invalid signature header format: {Error}", ex.Message);
            return (false, null);
        }

        actorUri = signature.KeyId.Split('#')[0];
        
        logger.LogDebug("Verifying signature for actor: {ActorUri}", actorUri);

        var keyPem = await GetOrFetchPublicKeyAsync(actorUri);
        if (string.IsNullOrEmpty(keyPem))
        {
            logger.LogWarning("Could not fetch public key for actor: {ActorUri}", actorUri);
            return (false, null);
        }

        try
        {
            var isValid = await HttpSignature.VerifyAsync(context, signature, null, keyPem);
            
            if (!isValid)
            {
                logger.LogWarning("Signature verification failed for actor: {ActorUri}", actorUri);
            }
            else
            {
                logger.LogInformation("Signature verified successfully for actor: {ActorUri}", actorUri);
            }
            
            return (isValid, isValid ? actorUri : null);
        }
        catch (HttpSignatureException ex)
        {
            logger.LogWarning("Signature validation error for actor {ActorUri}: {Error}", actorUri, ex.Message);
            return (false, null);
        }
    }

    public async Task SignOutgoingRequestAsync(
        HttpRequestMessage request,
        Guid publisherId
    )
    {
        var localKey = await keyService.GetKeyByPublisherAsync(publisherId);
        if (localKey == null || string.IsNullOrEmpty(localKey.PrivateKeyPem))
        {
            throw new InvalidOperationException($"No key found for publisher: {publisherId}");
        }

        var actorUri = await GetActorUriForPublisherAsync(publisherId);
        if (string.IsNullOrEmpty(actorUri))
        {
            throw new InvalidOperationException($"No actor URI found for publisher: {publisherId}");
        }

        var keyId = $"{actorUri}#main-key";
        
        await HttpSignature.SignRequestAsync(request, localKey.PrivateKeyPem, keyId);
    }

    public async Task SignOutgoingRequestAsync(
        HttpRequestMessage request,
        string actorUri
    )
    {
        if (!actorUri.StartsWith("https://"))
        {
            throw new InvalidOperationException($"Invalid actor URI: {actorUri}");
        }

        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor == null)
        {
            throw new InvalidOperationException($"Actor not found: {actorUri}");
        }

        if (!actor.PublisherId.HasValue)
        {
            throw new InvalidOperationException($"Actor has no publisher: {actorUri}");
        }

        await SignOutgoingRequestAsync(request, actor.PublisherId.Value);
    }

    private async Task<string?> GetActorUriForPublisherAsync(Guid publisherId)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
        
        return actor?.Uri;
    }

    private async Task<string?> GetOrFetchPublicKeyAsync(string keyId)
    {
        var actorUri = keyId.Split('#')[0];
        
        var cachedKey = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.KeyId == keyId);

        if (cachedKey != null && !string.IsNullOrEmpty(cachedKey.KeyPem))
        {
            return cachedKey.KeyPem;
        }

        var actor = await db.FediverseActors
            .IgnoreQueryFilters()
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor == null)
        {
            var domain = new Uri(actorUri).Host;
            var instance = await db.FediverseInstances
                .FirstOrDefaultAsync(i => i.Domain == domain);

            if (instance == null)
            {
                instance = new SnFediverseInstance
                {
                    Domain = domain,
                    Name = domain
                };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }

            actor = await discoveryService.GetOrCreateActorWithDataAsync(
                actorUri,
                actorUri.Split('/').Last(),
                instance.Id
            );
        }
        else
        {
            if (actor.DeletedAt != null)
            {
                actor.DeletedAt = null;
                await db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(actor.PublicKey))
            {
                await discoveryService.FetchActorDataAsync(actor);
            }
        }

        if (actor != null && !string.IsNullOrEmpty(actor.PublicKey))
        {
            return actor.PublicKey;
        }

        logger.LogWarning("No public key found for actor: {ActorUri}", actorUri);
        return null;
    }
}