using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[Flags]
public enum ResolveFlags
{
    None = 0,
    Uri = 1,
    OnlyExisting = 2,
    SkipCache = 4,
    ForceRefresh = 8
}

public class UserResolver(
    AppDatabase db,
    ActivityPubDiscoveryService discoveryService,
    ActivityPubKeyService keyService,
    FediverseCachingService cachingService,
    ILogger<UserResolver> logger
)
{
    public async Task<SnFediverseActor?> ResolveOrNullAsync(string uri, ResolveFlags flags = ResolveFlags.Uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        var useCache = !flags.HasFlag(ResolveFlags.SkipCache) && !flags.HasFlag(ResolveFlags.ForceRefresh);
        
        if (useCache)
        {
            var cached = await cachingService.GetActorByUriAsync(uri);
            if (cached != null)
                return await ResolveFromCacheAsync(cached);
        }

        if (flags.HasFlag(ResolveFlags.OnlyExisting))
        {
            var existing = await db.FediverseActors
                .Include(a => a.Instance)
                .FirstOrDefaultAsync(a => a.Uri == uri);
            
            if (existing != null)
                return existing;
            
            if (!flags.HasFlag(ResolveFlags.Uri))
                return null;
        }

        if (flags.HasFlag(ResolveFlags.Uri))
        {
            return await ResolveAsync(uri);
        }

        return null;
    }

    public async Task<SnFediverseActor> ResolveAsync(string uri)
    {
        logger.LogDebug("Resolving actor: {Uri}", uri);

        var existing = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == uri);

        if (existing != null)
        {
            logger.LogDebug("Actor found in database: {Uri}", uri);
            
            if (existing.DeletedAt != null)
            {
                existing.DeletedAt = null;
                await db.SaveChangesAsync();
            }

            return existing;
        }

        logger.LogInformation("Actor not found, fetching from remote: {Uri}", uri);
        
        var actor = await discoveryService.GetOrCreateActorWithDataAsync(
            uri,
            uri.Split('/').Last(),
            await GetOrCreateInstanceAsync(uri)
        );

        return actor;
    }

    public async Task<SnFediverseActor?> GetLocalActorAsync(Guid publisherId)
    {
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
    }

    public async Task<SnFediverseActor?> GetActorByUriAsync(string uri)
    {
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == uri);
    }

    public async Task<SnFediverseActor?> GetActorByUsernameAsync(string username, string? domain = null)
    {
        var query = db.FediverseActors.AsQueryable();
        
        if (!string.IsNullOrEmpty(domain))
        {
            query = query.Include(a => a.Instance)
                        .Where(a => a.Instance!.Domain == domain);
        }

        return await query.FirstOrDefaultAsync(a => a.Username == username);
    }

    public async Task InvalidateActorCacheAsync(string uri)
    {
        await cachingService.InvalidateActorAsync(Guid.Empty, uri: uri);
    }

    private async Task<SnFediverseActor> ResolveFromCacheAsync(CachedActor cached)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == cached.Id);

        return actor ?? await ResolveAsync(cached.Uri);
    }

    public async Task RefreshActorAsync(SnFediverseActor actor)
    {
        await discoveryService.FetchActorDataAsync(actor);
        await InvalidateActorCacheAsync(actor.Uri);
    }

    public async Task<bool> IsBlockedAsync(string actorUri)
    {
        var domain = ExtractDomain(actorUri);
        
        var rule = await db.FediverseModerationRules
            .FirstOrDefaultAsync(r => 
                r.Domain == domain && 
                r.Type == FediverseModerationRuleType.DomainBlock &&
                r.IsEnabled);

        return rule != null;
    }

    public async Task UpdateLastSeenAsync(SnFediverseActor actor)
    {
        actor.LastActivityAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> GetOrCreateInstanceAsync(string actorUri)
    {
        var domain = ExtractDomain(actorUri);
        
        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain == domain);

        if (instance != null)
            return instance.Id;

        instance = new SnFediverseInstance
        {
            Domain = domain,
            Name = domain
        };

        db.FediverseInstances.Add(instance);
        await db.SaveChangesAsync();

        return instance.Id;
    }

    private static string ExtractDomain(string uri)
    {
        try
        {
            return new Uri(uri).Host;
        }
        catch
        {
            return uri.Split('@').LastOrDefault() ?? "unknown";
        }
    }
}