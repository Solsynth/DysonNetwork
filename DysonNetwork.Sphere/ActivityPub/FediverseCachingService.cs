using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.ActivityPub;

public class FediverseCachingService(
    ICacheService cache,
    AppDatabase db,
    ILogger<FediverseCachingService> logger
)
{
    private const string ActorCacheGroup = "fediverse:actors";
    private const string InstanceCacheGroup = "fediverse:instances";
    private const string SearchCacheGroup = "fediverse:search";

    private static readonly TimeSpan ActorCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan InstanceCacheTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RelationshipCacheTtl = TimeSpan.FromMinutes(2);

    private string GetActorHandleCacheKey(string username, string instanceDomain) =>
        $"actor:handle:{username.ToLowerInvariant()}@{instanceDomain.ToLowerInvariant()}";

    private string GetActorUriCacheKey(string uri) => $"actor:uri:{uri.ToLowerInvariant()}";

    private string GetActorIdCacheKey(Guid id) => $"actor:id:{id}";

    private string GetInstanceDomainCacheKey(string domain) => $"instance:domain:{domain.ToLowerInvariant()}";

    private string GetSearchCacheKey(string query, int limit) =>
        $"search:{query.ToLowerInvariant()}:{limit}";

    private string GetRelationshipCacheKey(Guid actorId, Guid targetActorId) =>
        $"rel:{actorId}:{targetActorId}";

    public async Task<CachedActor?> GetActorByHandleAsync(string username, string instanceDomain)
    {
        var cacheKey = GetActorHandleCacheKey(username, instanceDomain);
        var cached = await cache.GetAsync<CachedActor>(cacheKey);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for actor handle: {Username}@{Domain}", username, instanceDomain);
            return cached;
        }
        logger.LogDebug("Cache miss for actor handle: {Username}@{Domain}", username, instanceDomain);
        return null;
    }

    public async Task<CachedActor?> GetActorByUriAsync(string uri)
    {
        var cacheKey = GetActorUriCacheKey(uri);
        var cached = await cache.GetAsync<CachedActor>(cacheKey);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for actor URI: {Uri}", uri);
            return cached;
        }
        return null;
    }

    public async Task<CachedActor?> GetActorByIdAsync(Guid id)
    {
        var cacheKey = GetActorIdCacheKey(id);
        var cached = await cache.GetAsync<CachedActor>(cacheKey);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for actor ID: {Id}", id);
            return cached;
        }
        return null;
    }

    public async Task SetActorAsync(SnFediverseActor actor, string instanceDomain)
    {
        var cached = new CachedActor
        {
            Id = actor.Id,
            Type = actor.Type,
            Uri = actor.Uri,
            Username = actor.Username,
            DisplayName = actor.DisplayName,
            Bio = actor.Bio,
            AvatarUrl = actor.AvatarUrl,
            HeaderUrl = actor.HeaderUrl,
            IsBot = actor.IsBot,
            IsLocked = actor.IsLocked,
            IsDiscoverable = actor.IsDiscoverable,
            InstanceDomain = instanceDomain,
            InstanceName = actor.Instance?.Name,
            InstanceSoftware = actor.Instance?.Software,
            LastActivityAt = actor.LastActivityAt,
            LastFetchedAt = actor.LastFetchedAt,
            FollowersCount = actor.FollowerRelationships?.Count ?? -1,
            FollowingCount = actor.FollowingRelationships?.Count ?? -1,
            Metadata = actor.Metadata
        };

        var handleCacheKey = GetActorHandleCacheKey(actor.Username, instanceDomain);
        var uriCacheKey = GetActorUriCacheKey(actor.Uri);
        var idCacheKey = GetActorIdCacheKey(actor.Id);

        await Task.WhenAll(
            cache.SetWithGroupsAsync(handleCacheKey, cached, new[] { ActorCacheGroup }, ActorCacheTtl),
            cache.SetWithGroupsAsync(uriCacheKey, cached, new[] { ActorCacheGroup }, ActorCacheTtl),
            cache.SetWithGroupsAsync(idCacheKey, cached, new[] { ActorCacheGroup }, ActorCacheTtl)
        );

        logger.LogDebug("Cached actor: {Username}@{Domain}", actor.Username, instanceDomain);
    }

    public async Task<CachedActor?> GetActorFromDbByHandleAsync(string username, string instanceDomain)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a =>
                a.Username.ToLower() == username.ToLower() &&
                a.Instance.Domain.ToLower() == instanceDomain.ToLower());

        if (actor == null)
            return null;

        await SetActorAsync(actor, instanceDomain);

        var followersCount = await db.FediverseRelationships
            .CountAsync(r => r.TargetActorId == actor.Id && r.State == RelationshipState.Accepted);

        var followingCount = await db.FediverseRelationships
            .CountAsync(r => r.ActorId == actor.Id && r.State == RelationshipState.Accepted);

        return new CachedActor
        {
            Id = actor.Id,
            Type = actor.Type,
            Uri = actor.Uri,
            Username = actor.Username,
            DisplayName = actor.DisplayName,
            Bio = actor.Bio,
            AvatarUrl = actor.AvatarUrl,
            HeaderUrl = actor.HeaderUrl,
            IsBot = actor.IsBot,
            IsLocked = actor.IsLocked,
            IsDiscoverable = actor.IsDiscoverable,
            InstanceDomain = instanceDomain,
            InstanceName = actor.Instance?.Name,
            InstanceSoftware = actor.Instance?.Software,
            LastActivityAt = actor.LastActivityAt,
            LastFetchedAt = actor.LastFetchedAt,
            FollowersCount = followersCount,
            FollowingCount = followingCount,
            Metadata = actor.Metadata
        };
    }

    public async Task<CachedActor?> GetActorFromDbByIdAsync(Guid id)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return null;

        await SetActorAsync(actor, actor.Instance?.Domain ?? "unknown");

        var followersCount = await db.FediverseRelationships
            .CountAsync(r => r.TargetActorId == actor.Id && r.State == RelationshipState.Accepted);

        var followingCount = await db.FediverseRelationships
            .CountAsync(r => r.ActorId == actor.Id && r.State == RelationshipState.Accepted);

        return new CachedActor
        {
            Id = actor.Id,
            Type = actor.Type,
            Uri = actor.Uri,
            Username = actor.Username,
            DisplayName = actor.DisplayName,
            Bio = actor.Bio,
            AvatarUrl = actor.AvatarUrl,
            HeaderUrl = actor.HeaderUrl,
            IsBot = actor.IsBot,
            IsLocked = actor.IsLocked,
            IsDiscoverable = actor.IsDiscoverable,
            InstanceDomain = actor.Instance?.Domain,
            InstanceName = actor.Instance?.Name,
            InstanceSoftware = actor.Instance?.Software,
            LastActivityAt = actor.LastActivityAt,
            LastFetchedAt = actor.LastFetchedAt,
            FollowersCount = followersCount,
            FollowingCount = followingCount,
            Metadata = actor.Metadata
        };
    }

    public async Task<CachedInstance?> GetInstanceByDomainAsync(string domain)
    {
        var cacheKey = GetInstanceDomainCacheKey(domain);
        var cached = await cache.GetAsync<CachedInstance>(cacheKey);
        if (cached != null)
        {
            logger.LogDebug("Cache hit for instance: {Domain}", domain);
            return cached;
        }

        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain.ToLower() == domain.ToLower());

        if (instance == null)
            return null;

        cached = new CachedInstance
        {
            Id = instance.Id,
            Domain = instance.Domain,
            Name = instance.Name,
            Description = instance.Description,
            Software = instance.Software,
            Version = instance.Version,
            IconUrl = instance.IconUrl,
            ThumbnailUrl = instance.ThumbnailUrl,
            ContactEmail = instance.ContactEmail,
            ContactAccountUsername = instance.ContactAccountUsername,
            ActiveUsers = instance.ActiveUsers,
            MetadataFetchedAt = instance.MetadataFetchedAt
        };

        await cache.SetWithGroupsAsync(cacheKey, cached, new[] { InstanceCacheGroup }, InstanceCacheTtl);
        logger.LogDebug("Cached instance: {Domain}", domain);

        return cached;
    }

    public async Task SetInstanceAsync(SnFediverseInstance instance)
    {
        var cached = new CachedInstance
        {
            Id = instance.Id,
            Domain = instance.Domain,
            Name = instance.Name,
            Description = instance.Description,
            Software = instance.Software,
            Version = instance.Version,
            IconUrl = instance.IconUrl,
            ThumbnailUrl = instance.ThumbnailUrl,
            ContactEmail = instance.ContactEmail,
            ContactAccountUsername = instance.ContactAccountUsername,
            ActiveUsers = instance.ActiveUsers,
            MetadataFetchedAt = instance.MetadataFetchedAt
        };

        var cacheKey = GetInstanceDomainCacheKey(instance.Domain);
        await cache.SetWithGroupsAsync(cacheKey, cached, new[] { InstanceCacheGroup }, InstanceCacheTtl);
        logger.LogDebug("Cached instance: {Domain}", instance.Domain);
    }

    public async Task<List<CachedActor>?> GetSearchResultsAsync(string query, int limit)
    {
        var cacheKey = GetSearchCacheKey(query, limit);
        return await cache.GetAsync<List<CachedActor>>(cacheKey);
    }

    public async Task SetSearchResultsAsync(string query, int limit, List<CachedActor> results)
    {
        var cacheKey = GetSearchCacheKey(query, limit);
        await cache.SetWithGroupsAsync(cacheKey, results, new[] { SearchCacheGroup }, SearchCacheTtl);
        logger.LogDebug("Cached search results for query: {Query}", query);
    }

    public async Task<CachedRelationship?> GetRelationshipAsync(Guid actorId, Guid targetActorId)
    {
        var cacheKey = GetRelationshipCacheKey(actorId, targetActorId);
        return await cache.GetAsync<CachedRelationship>(cacheKey);
    }

    public async Task SetRelationshipAsync(Guid actorId, Guid targetActorId, CachedRelationship relationship)
    {
        var cacheKey = GetRelationshipCacheKey(actorId, targetActorId);
        await cache.SetAsync(cacheKey, relationship, RelationshipCacheTtl);
    }

    public async Task InvalidateActorAsync(Guid actorId, string? username = null, string? instanceDomain = null, string? uri = null)
    {
        var tasks = new List<Task>();

        if (username != null && instanceDomain != null)
        {
            tasks.Add(cache.RemoveAsync(GetActorHandleCacheKey(username, instanceDomain)));
        }

        if (uri != null)
        {
            tasks.Add(cache.RemoveAsync(GetActorUriCacheKey(uri)));
        }

        tasks.Add(cache.RemoveAsync(GetActorIdCacheKey(actorId)));

        await Task.WhenAll(tasks);
        logger.LogDebug("Invalidated cache for actor: {ActorId}", actorId);
    }

    public async Task InvalidateInstanceAsync(string domain)
    {
        await cache.RemoveAsync(GetInstanceDomainCacheKey(domain));
        logger.LogDebug("Invalidated cache for instance: {Domain}", domain);
    }

    public async Task InvalidateSearchCacheAsync()
    {
        await cache.RemoveGroupAsync(SearchCacheGroup);
        logger.LogDebug("Invalidated all search cache");
    }
}

public class CachedActor
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "Person";
    public string Uri { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HeaderUrl { get; set; }
    public bool IsBot { get; set; }
    public bool IsLocked { get; set; }
    public bool IsDiscoverable { get; set; } = true;
    public string? InstanceDomain { get; set; }
    public string? InstanceName { get; set; }
    public string? InstanceSoftware { get; set; }
    public CachedInstance? Instance { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int PostCount { get; set; }
    public int? TotalPostCount { get; set; }
    public Instant? LastActivityAt { get; set; }
    public Instant? LastFetchedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string WebUrl => string.IsNullOrEmpty(Uri)
        ? $"https://{InstanceDomain}/@{Username}"
        : Uri.StartsWith("http") ? Uri.Replace("/users/", "/@") : $"https://{InstanceDomain}/@{Username}";
    public string FullHandle => $"{Username}@{InstanceDomain}";
}

public class CachedInstance
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = null!;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Software { get; set; }
    public string? Version { get; set; }
    public string? IconUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAccountUsername { get; set; }
    public int? ActiveUsers { get; set; }
    public Instant? MetadataFetchedAt { get; set; }
}

public class CachedRelationship
{
    public Guid ActorId { get; set; }
    public Guid TargetActorId { get; set; }
    public bool IsFollowing { get; set; }
    public bool IsFollowedBy { get; set; }
    public bool IsPending { get; set; }
}
