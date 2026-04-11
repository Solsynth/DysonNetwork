using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubKeyService(
    AppDatabase db,
    ILogger<ActivityPubKeyService> logger
) : IKeyService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ActorLocks = new();

    public async Task<SnFediverseKey> GetOrCreateKeyForActorAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256)
    {
        var actorLock = ActorLocks.GetOrAdd(actor.Id, _ => new SemaphoreSlim(1, 1));
        await actorLock.WaitAsync();
        try
        {
            return await GetOrCreateKeyForActorInternalAsync(actor, algorithm);
        }
        finally
        {
            actorLock.Release();
        }
    }

    private async Task<SnFediverseKey> GetOrCreateKeyForActorInternalAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existingKey = await db.FediverseKeys
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(k => k.ActorId == actor.Id);

            if (existingKey != null && !string.IsNullOrEmpty(existingKey.PrivateKeyPem))
                return existingKey;

            var (privateKey, publicKey) = HttpSignature.GenerateKeyPair();

            if (existingKey != null)
            {
                logger.LogInformation("Existing key for actor {ActorUri} has no private key, updating with new key", actor.Uri);
                existingKey.KeyPem = publicKey;
                existingKey.PrivateKeyPem = privateKey;
                existingKey.Algorithm = algorithm;
                existingKey.RotatedAt = SystemClock.Instance.GetCurrentInstant();
                existingKey.DeletedAt = null;

                try
                {
                    await db.SaveChangesAsync();
                    return existingKey;
                }
                catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    logger.LogWarning("Race condition updating key for actor {ActorUri}, retrying", actor.Uri);
                    await db.Entry(existingKey).ReloadAsync();
                    if (!string.IsNullOrEmpty(existingKey.PrivateKeyPem))
                        return existingKey;
                    continue;
                }
            }

            SnFediverseKey? keyToAdd = null;
            try
            {
                keyToAdd = new SnFediverseKey
                {
                    KeyId = $"{actor.Uri}#main-key",
                    KeyPem = publicKey,
                    PrivateKeyPem = privateKey,
                    Algorithm = algorithm,
                    ActorId = actor.Id,
                    PublisherId = actor.PublisherId,
                    CreatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                db.FediverseKeys.Add(keyToAdd);
                await db.SaveChangesAsync();

                logger.LogInformation("Generated new key pair for actor: {ActorUri} with algorithm {Algorithm}", actor.Uri, algorithm);
                return keyToAdd;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                logger.LogWarning("Race condition inserting key for actor {ActorUri}, retrying", actor.Uri);
                if (keyToAdd != null)
                    db.Entry(keyToAdd).State = EntityState.Detached;

                var conflictingKey = await db.FediverseKeys
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(k => k.KeyId == $"{actor.Uri}#main-key");
                if (conflictingKey != null && !string.IsNullOrEmpty(conflictingKey.PrivateKeyPem))
                {
                    logger.LogInformation("Race condition resolved: found existing key for actor {ActorUri}", actor.Uri);
                    return conflictingKey;
                }
                continue;
            }
        }

        var finalKey = await db.FediverseKeys.FirstOrDefaultAsync(k => k.ActorId == actor.Id);
        if (finalKey != null && !string.IsNullOrEmpty(finalKey.PrivateKeyPem))
            return finalKey;

        logger.LogError("Failed to create or update key for actor {ActorUri} after retries", actor.Uri);
        throw new InvalidOperationException($"Failed to create key for actor {actor.Uri}");
    }

    public async Task<SnFediverseKey?> GetKeyForActorAsync(Guid actorId)
    {
        return await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actorId);
    }

    public async Task<SnFediverseKey?> GetKeyForActorAsync(string actorUri)
    {
        return await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.KeyId == $"{actorUri}#main-key");
    }

    public async Task<SnFediverseKey> CreateKeyForActorAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256)
    {
        return await GetOrCreateKeyForActorAsync(actor, algorithm);
    }

    public async Task RotateKeyAsync(Guid actorId)
    {
        var actor = await db.FediverseActors.FindAsync(actorId);
        if (actor == null)
            return;

        await UpdateKeyForActorAsync(actor);
    }

    public async Task<(string publicKeyPem, string privateKeyPem)?> GetKeyPairForActorAsync(Guid actorId)
    {
        var key = await GetKeyForActorAsync(actorId);
        if (key == null || string.IsNullOrEmpty(key.PrivateKeyPem))
            return null;

        return (key.KeyPem, key.PrivateKeyPem);
    }

    public async Task<SnFediverseKey?> GetKeyByKeyIdAsync(string keyId)
    {
        return await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.KeyId == keyId);
    }

    public async Task<SnFediverseKey?> GetKeyByPublisherAsync(Guid publisherId)
    {
        return await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.PublisherId == publisherId);
    }

    public async Task UpdateKeyForActorAsync(SnFediverseActor actor)
    {
        var actorLock = ActorLocks.GetOrAdd(actor.Id, _ => new SemaphoreSlim(1, 1));
        await actorLock.WaitAsync();
        try
        {
            await UpdateKeyForActorInternalAsync(actor);
        }
        finally
        {
            actorLock.Release();
        }
    }

    private async Task UpdateKeyForActorInternalAsync(SnFediverseActor actor)
    {
        var existingKey = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actor.Id);

        if (existingKey != null && !string.IsNullOrEmpty(existingKey.PrivateKeyPem))
        {
            logger.LogInformation("Actor already has a key pair: {ActorUri}", actor.Uri);
            return;
        }

        var (privateKey, publicKey) = HttpSignature.GenerateKeyPair();

        if (existingKey != null)
        {
            existingKey.KeyPem = publicKey;
            existingKey.PrivateKeyPem = privateKey;
            existingKey.RotatedAt = SystemClock.Instance.GetCurrentInstant();
        }
        else
        {
            var key = new SnFediverseKey
            {
                KeyId = $"{actor.Uri}#main-key",
                KeyPem = publicKey,
                PrivateKeyPem = privateKey,
                ActorId = actor.Id,
                PublisherId = actor.PublisherId,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            db.FediverseKeys.Add(key);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Generated new key pair for actor: {ActorUri}", actor.Uri);
    }

    public async Task StoreRemoteKeyAsync(string keyId, string keyPem, Guid actorId)
    {
        var existingKey = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.KeyId == keyId);

        if (existingKey != null)
        {
            existingKey.KeyPem = keyPem;
            existingKey.ActorId = actorId;
            existingKey.RotatedAt = SystemClock.Instance.GetCurrentInstant();
        }
        else
        {
            var key = new SnFediverseKey
            {
                KeyId = keyId,
                KeyPem = keyPem,
                ActorId = actorId,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            db.FediverseKeys.Add(key);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Stored remote key: {KeyId}", keyId);
    }

    public static string Sign(string privateKeyPem, string dataToSign)
    {
        using var rsa = RSA.Create();
        ImportPrivateKey(rsa, privateKeyPem);
        
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
            using var rsa = RSA.Create();
            ImportPublicKey(rsa, publicKeyPem);
            
            var signature = Convert.FromBase64String(signatureBase64);
            
            var result = rsa.VerifyData(
                Encoding.UTF8.GetBytes(data),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify signature");
            return false;
        }
    }

    private static void ImportPrivateKey(RSA rsa, string privateKeyPem)
    {
        var lines = privateKeyPem.Split('\n')
            .Where(line => !line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var keyBytes = Convert.FromBase64String(string.Join("", lines));
        rsa.ImportRSAPrivateKey(keyBytes, out _);
    }

    private static void ImportPublicKey(RSA rsa, string publicKeyPem)
    {
        var lines = publicKeyPem.Split('\n')
            .Where(line => !line.StartsWith("-----") && !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var keyBytes = Convert.FromBase64String(string.Join("", lines));

        var isRsaPublicKey = publicKeyPem.Contains("-----BEGIN RSA PUBLIC KEY-----");

        if (isRsaPublicKey)
        {
            rsa.ImportRSAPublicKey(keyBytes, out _);
        }
        else
        {
            rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
        }
    }
}