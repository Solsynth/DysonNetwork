namespace DysonNetwork.Sphere.ActivityPub;

public interface IServerSigningKeyService
{
    string KeyId { get; }
    string ActorUri { get; }
    Task<(string publicKey, string privateKey)> GetOrCreateKeyAsync();
    Task<string?> GetPublicKeyAsync();
    Task InvalidateCacheAsync();
    Task RotateKeyAsync();
}
