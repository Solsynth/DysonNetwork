using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Models;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public interface IActorDiscoveryService
{
    Task<SnPublisher> GetOrCreateActorWithDataAsync(string actorUri, string username, Guid instanceId);
    Task<SnPublisher?> GetOrCreateActorAsync(string actorUri, string? username = null, Guid? instanceId = null);
    Task FetchActorDataAsync(SnPublisher actor);
    Task<Dictionary<string, object>?> FetchActivityAsync(string uri, string? actorUri);
    Task<SnPublisher?> DiscoverActorAsync(string query);
    Task<List<SnPublisher>> SearchActorsAsync(string query, int limit = 20, bool includeRemoteDiscovery = false);
    Task FetchActorStatsAsync(SnPublisher actor);
    Task FetchInstanceMetadataAsync(SnFediverseInstance instance);
}