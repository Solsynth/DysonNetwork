using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Security.Cryptography;
using System.Text;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubSignatureService(
    AppDatabase db,
    ActivityPubKeyService keyService,
    IServerSigningKeyService serverKeyService,
    ILogger<ActivityPubSignatureService> logger,
    IConfiguration configuration,
    IServiceProvider serviceProvider
) : ISignatureService
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    private IActorDiscoveryService? _discoveryService;
    private IActorDiscoveryService DiscoveryService => _discoveryService ??= serviceProvider.GetRequiredService<IActorDiscoveryService>();

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
            logger.LogDebug("Parsed signature. KeyId: {KeyId}, Algorithm: {Algorithm}, Headers: {Headers}", 
                signature.KeyId, signature.Algorithm, string.Join(" ", signature.Headers));
        }
        catch (HttpSignatureException ex)
        {
            logger.LogWarning("Invalid signature header format: {Error}", ex.Message);
            return (false, null);
        }

        actorUri = signature.KeyId.Split('#')[0];
        
        logger.LogDebug("Extracted actorUri from keyId: {ActorUri} (from {KeyId})", actorUri, signature.KeyId);

        var keyPem = await GetOrFetchPublicKeyAsync(signature.KeyId);
        if (string.IsNullOrEmpty(keyPem))
        {
            logger.LogWarning("Could not fetch public key for keyId: {KeyId}", signature.KeyId);
            return (false, null);
        }

        logger.LogDebug("Got public key for verification. Key length: {Length}", keyPem?.Length ?? 0);

        try
        {
            var isValid = await HttpSignature.VerifyAsync(context, signature, null, keyPem);
            
            if (!isValid)
            {
                logger.LogWarning("Signature verification failed for actor: {ActorUri} (keyId: {KeyId})", actorUri, signature.KeyId);
                logger.LogDebug("Request details - Method: {Method}, Path: {Path}, Query: {Query}, Host: {Host}", 
                    context.Request.Method, context.Request.Path, context.Request.QueryString, context.Request.Host);
            }
            else
            {
                logger.LogInformation("Signature verified successfully for actor: {ActorUri}", actorUri);
            }
            
            return (isValid, isValid ? actorUri : null);
        }
        catch (HttpSignatureException ex)
        {
            logger.LogWarning("Signature validation error for actor {ActorUri} (keyId: {KeyId}): {Error}", 
                actorUri, signature.KeyId, ex.Message);
            return (false, null);
        }
    }

    public async Task SignOutgoingRequestAsync(
        HttpRequestMessage request,
        Guid publisherId
    )
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
        
        if (actor == null)
        {
            logger.LogWarning("No actor found for publisher: {PublisherId}, falling back to server key", publisherId);
            await SignOutgoingRequestWithServerKeyAsync(request);
            return;
        }

        var localKey = await keyService.GetKeyForActorAsync(actor.Id);
        if (localKey == null || string.IsNullOrEmpty(localKey.PrivateKeyPem))
        {
            logger.LogInformation("No key found for actor {ActorUri}, auto-creating key", actor.Uri);
            localKey = await keyService.GetOrCreateKeyForActorAsync(actor);
        }

        if (localKey == null || string.IsNullOrEmpty(localKey.PrivateKeyPem))
        {
            logger.LogWarning("Still no valid key for actor {ActorUri}, falling back to server key", actor.Uri);
            await SignOutgoingRequestWithServerKeyAsync(request);
            return;
        }

        var keyId = $"{actor.Uri}#main-key";
        
        await HttpSignature.SignRequestAsync(request, localKey.PrivateKeyPem, keyId);
    }

    public async Task SignOutgoingRequestAsync(
        HttpRequestMessage request,
        string actorUri
    )
    {
        if (!actorUri.StartsWith("https://"))
        {
            logger.LogWarning("Invalid actor URI: {ActorUri}, using server key", actorUri);
            await SignOutgoingRequestWithServerKeyAsync(request);
            return;
        }

        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor == null || !actor.PublisherId.HasValue)
        {
            logger.LogDebug("Actor {ActorUri} not found or has no publisher, using server key", actorUri);
            await SignOutgoingRequestWithServerKeyAsync(request);
            return;
        }

        await SignOutgoingRequestAsync(request, actor.PublisherId.Value);
    }

    public async Task SignOutgoingRequestWithServerKeyAsync(HttpRequestMessage request)
    {
        try
        {
            var (publicKey, privateKey) = await serverKeyService.GetOrCreateKeyAsync();
            var keyId = serverKeyService.KeyId;

            logger.LogDebug("Signing request with server key. KeyId: {KeyId}, Target: {Url}", 
                keyId, request.RequestUri);

            await HttpSignature.SignRequestAsync(request, privateKey, keyId);

            logger.LogDebug("Request signed successfully with server key: {KeyId}", keyId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sign request with server key");
            throw;
        }
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
            logger.LogDebug("Found cached key for keyId: {KeyId}", keyId);
            return cachedKey.KeyPem;
        }

        logger.LogDebug("No cached key for keyId: {KeyId}, looking up actor by URI: {ActorUri}", keyId, actorUri);

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

            actor = await DiscoveryService.GetOrCreateActorWithDataAsync(
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
                await DiscoveryService.FetchActorDataAsync(actor);
            }
        }

        if (actor != null && !string.IsNullOrEmpty(actor.PublicKey))
        {
            logger.LogDebug("Got public key from actor {ActorUri}. Key length: {Length}", 
                actorUri, actor.PublicKey.Length);
            return actor.PublicKey;
        }

        logger.LogWarning("No public key found for actor: {ActorUri}", actorUri);
        return null;
    }
}