using DysonNetwork.Shared.Models;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public interface IKeyService
{
    Task<SnFediverseKey?> GetKeyForActorAsync(Guid actorId);
    Task<SnFediverseKey?> GetKeyForActorAsync(string actorUri);
    Task<SnFediverseKey> GetOrCreateKeyForActorAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256);
    Task<SnFediverseKey> CreateKeyForActorAsync(SnFediverseActor actor, string algorithm = KeyAlgorithm.RSA_SHA256);
    Task RotateKeyAsync(Guid actorId);
    Task<(string publicKeyPem, string privateKeyPem)?> GetKeyPairForActorAsync(Guid actorId);
}
