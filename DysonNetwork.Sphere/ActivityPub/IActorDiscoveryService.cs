using DysonNetwork.Shared.Models;

namespace DysonNetwork.Sphere.ActivityPub;

public interface IActorDiscoveryService
{
    Task<SnFediverseActor> GetOrCreateActorWithDataAsync(string actorUri, string username, Guid instanceId);
    Task<SnFediverseActor?> GetOrCreateActorAsync(string actorUri, string? username = null, Guid? instanceId = null);
    Task FetchActorDataAsync(SnFediverseActor actor);
    Task<Dictionary<string, object>?> FetchActivityAsync(string uri, string actorUri);
    Task<SnFediverseActor?> DiscoverActorAsync(string query);
    Task<List<SnFediverseActor>> SearchActorsAsync(string query, int limit = 20, bool includeRemoteDiscovery = false);
    Task FetchActorStatsAsync(SnFediverseActor actor);
}