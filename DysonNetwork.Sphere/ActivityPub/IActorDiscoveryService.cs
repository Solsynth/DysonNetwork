using DysonNetwork.Shared.Models;

namespace DysonNetwork.Sphere.ActivityPub;

public interface IActorDiscoveryService
{
    Task<SnFediverseActor> GetOrCreateActorWithDataAsync(string actorUri, string username, Guid instanceId);
    Task FetchActorDataAsync(SnFediverseActor actor);
    Task<Dictionary<string, object>?> FetchActivityAsync(string uri, string actorUri);
}