using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Permission;

public class PermissionService(
    AppDatabase db,
    ICacheService cache
)
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(1);

    private const string PermCacheKeyPrefix = "perm:";
    private const string PermGroupCacheKeyPrefix = "perm-cg:";
    private const string PermissionGroupPrefix = "perm-g:";

    private static string _GetPermissionCacheKey(string actor, string area, string key) =>
        PermCacheKeyPrefix + actor + ":" + area + ":" + key;

    private static string _GetGroupsCacheKey(string actor) =>
        PermGroupCacheKeyPrefix + actor;

    private static string _GetPermissionGroupKey(string actor) =>
        PermissionGroupPrefix + actor;

    public async Task<bool> HasPermissionAsync(string actor, string area, string key)
    {
        var value = await GetPermissionAsync<bool>(actor, area, key);
        return value;
    }

    public async Task<T?> GetPermissionAsync<T>(string actor, string area, string key)
    {
        var cacheKey = _GetPermissionCacheKey(actor, area, key);

        var (hit, cachedValue) = await cache.GetAsyncWithStatus<T>(cacheKey);
        if (hit)
            return cachedValue;
        
        var now = SystemClock.Instance.GetCurrentInstant();
        var groupsKey = _GetGroupsCacheKey(actor);

        var groupsId = await cache.GetAsync<List<Guid>>(groupsKey);
        if (groupsId == null)
        {
            groupsId = await db.PermissionGroupMembers
                .Where(n => n.Actor == actor)
                .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
                .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
                .Select(e => e.GroupId)
                .ToListAsync();

            await cache.SetWithGroupsAsync(groupsKey, groupsId,
                [_GetPermissionGroupKey(actor)],
                CacheExpiration);
        }

        var permission = await db.PermissionNodes
            .Where(n => (n.GroupId == null && n.Actor == actor) ||
                        (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
            .Where(n => n.Key == key && n.Area == area)
            .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
            .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
            .FirstOrDefaultAsync();

        var result = permission is not null ? _DeserializePermissionValue<T>(permission.Value) : default;

        await cache.SetWithGroupsAsync(cacheKey, result,
            [_GetPermissionGroupKey(actor)],
            CacheExpiration);

        return result;
    }

    public async Task<SnPermissionNode> AddPermissionNode<T>(
        string actor,
        string area,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new SnPermissionNode
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
        await InvalidatePermissionCacheAsync(actor, area, key);

        return node;
    }

    public async Task<SnPermissionNode> AddPermissionNodeToGroup<T>(
        SnPermissionGroup group,
        string actor,
        string area,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new SnPermissionNode
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
        await InvalidatePermissionCacheAsync(actor, area, key);
        await cache.RemoveAsync(_GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(_GetPermissionGroupKey(actor));

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
        await InvalidatePermissionCacheAsync(actor, area, key);
    }

    public async Task RemovePermissionNodeFromGroup<T>(SnPermissionGroup group, string actor, string area, string key)
    {
        var node = await db.PermissionNodes
            .Where(n => n.GroupId == group.Id)
            .Where(n => n.Actor == actor && n.Area == area && n.Key == key)
            .FirstOrDefaultAsync();
        if (node is null) return;
        db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();

        // Invalidate caches
        await InvalidatePermissionCacheAsync(actor, area, key);
        await cache.RemoveAsync(_GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(_GetPermissionGroupKey(actor));
    }

    private async Task InvalidatePermissionCacheAsync(string actor, string area, string key)
    {
        var cacheKey = _GetPermissionCacheKey(actor, area, key);
        await cache.RemoveAsync(cacheKey);
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

    public static SnPermissionNode NewPermissionNode<T>(string actor, string area, string key, T value)
    {
        return new SnPermissionNode
        {
            Actor = actor,
            Area = area,
            Key = key,
            Value = _SerializePermissionValue(value),
        };
    }
}