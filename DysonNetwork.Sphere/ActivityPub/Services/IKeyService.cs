using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Models;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public interface IKeyService
{
    Task<SnFediverseKey?> GetKeyForActorAsync(Guid actorId);
    Task<SnFediverseKey?> GetKeyForActorAsync(string actorUri);
    Task<SnFediverseKey> GetOrCreateKeyForActorAsync(SnPublisher actor, string algorithm = KeyAlgorithm.RSA_SHA256);
    Task<SnFediverseKey> CreateKeyForActorAsync(SnPublisher actor, string algorithm = KeyAlgorithm.RSA_SHA256);
    Task RotateKeyAsync(Guid actorId);
    Task<(string publicKeyPem, string privateKeyPem)?> GetKeyPairForActorAsync(Guid actorId);
}
