using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubKeyService(
    AppDatabase db,
    ILogger<ActivityPubKeyService> logger
)
{
    public async Task<SnFediverseKey> GetOrCreateKeyForActorAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256)
    {
        var existingKey = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actor.Id);

        if (existingKey != null)
            return existingKey;

        var (privateKey, publicKey) = HttpSignature.GenerateKeyPair();

        var key = new SnFediverseKey
        {
            KeyId = $"{actor.Uri}#main-key",
            KeyPem = publicKey,
            PrivateKeyPem = privateKey,
            Algorithm = algorithm,
            ActorId = actor.Id,
            PublisherId = actor.PublisherId,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.FediverseKeys.Add(key);
        await db.SaveChangesAsync();

        logger.LogInformation("Generated new key pair for actor: {ActorUri} with algorithm {Algorithm}", actor.Uri, algorithm);
        return key;
    }

    public async Task<SnFediverseKey?> GetKeyForActorAsync(Guid actorId)
    {
        return await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actorId);
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