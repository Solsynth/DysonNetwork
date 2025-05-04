using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Permission;

public class PermissionService(
    AppDatabase db,
    IMemoryCache cache,
    ILogger<PermissionService> logger)
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(1);

    private string GetPermissionCacheKey(string actor, string area, string key) => 
        $"perm:{actor}:{area}:{key}";

    private string GetGroupsCacheKey(string actor) => 
        $"perm_groups:{actor}";

    public async Task<bool> HasPermissionAsync(string actor, string area, string key)
    {
        var value = await GetPermissionAsync<bool>(actor, area, key);
        return value;
    }

    public async Task<T?> GetPermissionAsync<T>(string actor, string area, string key)
    {
        var cacheKey = GetPermissionCacheKey(actor, area, key);
        
        if (cache.TryGetValue<T>(cacheKey, out var cachedValue))
        {
            return cachedValue;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var groupsKey = GetGroupsCacheKey(actor);
        
        var groupsId = await cache.GetOrCreateAsync(groupsKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheExpiration;
            return await db.PermissionGroupMembers
                .Where(n => n.Actor == actor)
                .Where(n => n.ExpiredAt == null || n.ExpiredAt < now)
                .Where(n => n.AffectedAt == null || n.AffectedAt >= now)
                .Select(e => e.GroupId)
                .ToListAsync();
        });

        var permission = await db.PermissionNodes
            .Where(n => n.GroupId == null || groupsId.Contains(n.GroupId.Value))
            .Where(n => (n.Key == key || n.Key == "*") && (n.GroupId != null || n.Actor == actor) && n.Area == area)
            .Where(n => n.ExpiredAt == null || n.ExpiredAt < now)
            .Where(n => n.AffectedAt == null || n.AffectedAt >= now)
            .FirstOrDefaultAsync();

        var result = permission is not null ? _DeserializePermissionValue<T>(permission.Value) : default;

        cache.Set(cacheKey, result, CacheExpiration);
        
        return result;
    }

    public async Task<PermissionNode> AddPermissionNode<T>(
        string actor,
        string area,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new PermissionNode
        {
            Actor = actor,
            Key = key,
            Area = area,
            Value = _SerializePermissionValue(value),
            ExpiredAt = expiredAt,
            AffectedAt = affectedAt
        };

        db.PermissionNodes.Add(node);
        await db.SaveChangesAsync();

        // Invalidate related caches
        InvalidatePermissionCache(actor, area, key);

        return node;
    }

    public async Task<PermissionNode> AddPermissionNodeToGroup<T>(
        PermissionGroup group,
        string actor,
        string area,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new PermissionNode
        {
            Actor = actor,
            Key = key,
            Area = area,
            Value = _SerializePermissionValue(value),
            ExpiredAt = expiredAt,
            AffectedAt = affectedAt,
            Group = group,
            GroupId = group.Id
        };

        db.PermissionNodes.Add(node);
        await db.SaveChangesAsync();

        // Invalidate related caches
        InvalidatePermissionCache(actor, area, key);
        cache.Remove(GetGroupsCacheKey(actor));

        return node;
    }

    public async Task RemovePermissionNode(string actor, string area, string key)
    {
        var node = await db.PermissionNodes
            .Where(n => n.Actor == actor && n.Area == area && n.Key == key)
            .FirstOrDefaultAsync();
        if (node is not null) db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();

        // Invalidate cache
        InvalidatePermissionCache(actor, area, key);
    }

    public async Task RemovePermissionNodeFromGroup<T>(PermissionGroup group, string actor, string area, string key)
    {
        var node = await db.PermissionNodes
            .Where(n => n.GroupId == group.Id)
            .Where(n => n.Actor == actor && n.Area == area && n.Key == key)
            .FirstOrDefaultAsync();
        if (node is null) return;
        db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();

        // Invalidate caches
        InvalidatePermissionCache(actor, area, key);
        cache.Remove(GetGroupsCacheKey(actor));
    }

    private void InvalidatePermissionCache(string actor, string area, string key)
    {
        var cacheKey = GetPermissionCacheKey(actor, area, key);
        cache.Remove(cacheKey);
    }

    private static T? _DeserializePermissionValue<T>(JsonDocument json)
    {
        return JsonSerializer.Deserialize<T>(json.RootElement.GetRawText());
    }

    private static JsonDocument _SerializePermissionValue<T>(T obj)
    {
        var str = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(str);
    }

    public static PermissionNode NewPermissionNode<T>(string actor, string area, string key, T value)
    {
        return new PermissionNode
        {
            Actor = actor,
            Area = area,
            Key = key,
            Value = _SerializePermissionValue(value),
        };
    }
}