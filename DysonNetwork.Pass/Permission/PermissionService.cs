using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Permission;

public class PermissionServiceOptions
{
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableWildcardMatching { get; set; } = true;
    public int MaxWildcardMatches { get; set; } = 100;
}

public class PermissionService(
    AppDatabase db,
    ICacheService cache,
    ILogger<PermissionService> logger,
    IOptions<PermissionServiceOptions> options
)
{
    private readonly PermissionServiceOptions _options = options.Value;

    private const string PermissionCacheKeyPrefix = "perm:";
    private const string PermissionGroupCacheKeyPrefix = "perm-cg:";
    private const string PermissionGroupPrefix = "perm-g:";

    private static string GetPermissionCacheKey(string actor, string key) =>
        PermissionCacheKeyPrefix + actor + ":" + key;

    private static string GetGroupsCacheKey(string actor) =>
        PermissionGroupCacheKeyPrefix + actor;

    private static string GetPermissionGroupKey(string actor) =>
        PermissionGroupPrefix + actor;

    public async Task<bool> HasPermissionAsync(
        string actor,
        string key,
        PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        var value = await GetPermissionAsync<bool>(actor, key, type);
        return value;
    }

    public async Task<T?> GetPermissionAsync<T>(
        string actor,
        string key,
        PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor cannot be null or empty", nameof(actor));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var cacheKey = GetPermissionCacheKey(actor, key);

        try
        {
            var (hit, cachedValue) = await cache.GetAsyncWithStatus<T>(cacheKey);
            if (hit)
            {
                logger.LogDebug("Permission cache hit for {Type}:{Actor}:{Key}", type, actor, key);
                return cachedValue;
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            var groupsId = await GetOrCacheUserGroupsAsync(actor, now);

            var permission = await FindPermissionNodeAsync(type, actor, key, groupsId);
            var result = permission != null ? DeserializePermissionValue<T>(permission.Value) : default;

            await cache.SetWithGroupsAsync(cacheKey, result,
                [GetPermissionGroupKey(actor)],
                _options.CacheExpiration);

            logger.LogDebug("Permission resolved for {Type}:{Actor}:{Key} = {Result}", type, actor, key,
                result != null);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving permission for {Type}:{Actor}:{Key}", type, actor, key);
            throw;
        }
    }

    private async Task<List<Guid>> GetOrCacheUserGroupsAsync(string actor, Instant now)
    {
        var groupsKey = GetGroupsCacheKey(actor);

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
                [GetPermissionGroupKey(actor)],
                _options.CacheExpiration);

            logger.LogDebug("Cached {Count} groups for actor {Actor}", groupsId.Count, actor);
        }

        return groupsId;
    }

    private async Task<SnPermissionNode?> FindPermissionNodeAsync(
        PermissionNodeActorType type,
        string actor,
        string key,
        List<Guid> groupsId
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        // First try exact match (highest priority)
        var exactMatch = await db.PermissionNodes
            .Where(n => (n.GroupId == null && n.Actor == actor && n.Type == type) ||
                        (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
            .Where(n => n.Key == key)
            .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
            .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
            .FirstOrDefaultAsync();

        if (exactMatch != null)
            return exactMatch;

        // If no exact match and wildcards are enabled, try wildcard matches
        if (!_options.EnableWildcardMatching)
            return null;

        var wildcardMatches = await db.PermissionNodes
            .Where(n => (n.GroupId == null && n.Actor == actor && n.Type == type) ||
                        (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
            .Where(n => EF.Functions.Like(n.Key, "%*%"))
            .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
            .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
            .Take(_options.MaxWildcardMatches)
            .ToListAsync();

        // Find the best wildcard match
        SnPermissionNode? bestMatch = null;
        var bestMatchScore = -1;

        foreach (var node in wildcardMatches)
        {
            var score = CalculateWildcardMatchScore(node.Key, key);
            if (score <= bestMatchScore) continue;
            bestMatch = node;
            bestMatchScore = score;
        }

        if (bestMatch != null)
            logger.LogDebug("Found wildcard permission match: {NodeKey} for {Key}", bestMatch.Key, key);

        return bestMatch;
    }

    private static int CalculateWildcardMatchScore(string nodeKey, string targetKey)
    {
        return CalculatePatternMatchScore(nodeKey, targetKey);
    }

    private static int CalculatePatternMatchScore(string pattern, string target)
    {
        if (pattern == target)
            return int.MaxValue; // Exact match

        if (!pattern.Contains('*'))
            return -1; // No wildcard, not a match

        // Simple wildcard matching: * matches any sequence of characters
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!regex.IsMatch(target)) return -1; // No match

        // Score based on specificity (shorter patterns are less specific)
        var wildcardCount = pattern.Count(c => c == '*');
        var length = pattern.Length;

        return Math.Max(1, 1000 - wildcardCount * 100 - length);
    }

    public async Task<SnPermissionNode> AddPermissionNode<T>(
        string actor,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null,
        PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new SnPermissionNode
        {
            Actor = actor,
            Type = type,
            Key = key,
            Value = SerializePermissionValue(value),
            ExpiredAt = expiredAt,
            AffectedAt = affectedAt
        };

        db.PermissionNodes.Add(node);
        await db.SaveChangesAsync();

        // Invalidate related caches
        await InvalidatePermissionCacheAsync(actor, key);

        return node;
    }

    public async Task<SnPermissionNode> AddPermissionNodeToGroup<T>(
        SnPermissionGroup group,
        string actor,
        string key,
        T value,
        Instant? expiredAt = null,
        Instant? affectedAt = null,
        PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var node = new SnPermissionNode
        {
            Actor = actor,
            Type = type,
            Key = key,
            Value = SerializePermissionValue(value),
            ExpiredAt = expiredAt,
            AffectedAt = affectedAt,
            Group = group,
            GroupId = group.Id
        };

        db.PermissionNodes.Add(node);
        await db.SaveChangesAsync();

        // Invalidate related caches
        await InvalidatePermissionCacheAsync(actor, key);
        await cache.RemoveAsync(GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(GetPermissionGroupKey(actor));

        return node;
    }

    public async Task RemovePermissionNode(string actor, string key, PermissionNodeActorType? type)
    {
        var node = await db.PermissionNodes
            .Where(n => n.Actor == actor && n.Key == key)
            .If(type is not null, q => q.Where(n => n.Type == type))
            .FirstOrDefaultAsync();
        if (node is not null) db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();

        // Invalidate cache
        await InvalidatePermissionCacheAsync(actor, key);
    }

    public async Task RemovePermissionNodeFromGroup<T>(SnPermissionGroup group, string actor, string key)
    {
        var node = await db.PermissionNodes
            .Where(n => n.GroupId == group.Id)
            .Where(n => n.Actor == actor && n.Key == key && n.Type == PermissionNodeActorType.Group)
            .FirstOrDefaultAsync();
        if (node is null) return;
        db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();

        // Invalidate caches
        await InvalidatePermissionCacheAsync(actor, key);
        await cache.RemoveAsync(GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(GetPermissionGroupKey(actor));
    }

    private async Task InvalidatePermissionCacheAsync(string actor, string key)
    {
        var cacheKey = GetPermissionCacheKey(actor, key);
        await cache.RemoveAsync(cacheKey);
    }

    private static T? DeserializePermissionValue<T>(JsonDocument json)
    {
        return JsonSerializer.Deserialize<T>(json.RootElement.GetRawText());
    }

    private static JsonDocument SerializePermissionValue<T>(T obj)
    {
        var str = JsonSerializer.Serialize(obj);
        return JsonDocument.Parse(str);
    }

    public static SnPermissionNode NewPermissionNode<T>(string actor, string key, T value)
    {
        return new SnPermissionNode
        {
            Actor = actor,
            Key = key,
            Value = SerializePermissionValue(value),
        };
    }

    /// <summary>
    /// Lists all effective permissions for an actor (including group permissions)
    /// </summary>
    public async Task<List<SnPermissionNode>> ListEffectivePermissionsAsync(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor cannot be null or empty", nameof(actor));

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var groupsId = await GetOrCacheUserGroupsAsync(actor, now);

            var permissions = await db.PermissionNodes
                .Where(n => (n.GroupId == null && n.Actor == actor) ||
                            (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
                .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
                .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
                .OrderBy(n => n.Key)
                .ToListAsync();

            logger.LogDebug("Listed {Count} effective permissions for actor {Actor}", permissions.Count, actor);
            return permissions;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing permissions for actor {Actor}", actor);
            throw;
        }
    }

    /// <summary>
    /// Lists all direct permissions for an actor (excluding group permissions)
    /// </summary>
    public async Task<List<SnPermissionNode>> ListDirectPermissionsAsync(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor cannot be null or empty", nameof(actor));

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var permissions = await db.PermissionNodes
                .Where(n => n.GroupId == null && n.Actor == actor)
                .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
                .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
                .OrderBy(n => n.Key)
                .ToListAsync();

            logger.LogDebug("Listed {Count} direct permissions for actor {Actor}", permissions.Count, actor);
            return permissions;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing direct permissions for actor {Actor}", actor);
            throw;
        }
    }

    /// <summary>
    /// Validates a permission pattern for wildcard usage
    /// </summary>
    public static bool IsValidPermissionPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        // Basic validation: no consecutive wildcards, no leading/trailing wildcards in some cases
        if (pattern.Contains("**") || pattern.StartsWith("*") || pattern.EndsWith("*"))
            return false;

        // Check for valid characters (alphanumeric, underscore, dash, dot, star)
        return pattern.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '*' || c == ':');
    }

    /// <summary>
    /// Clears all cached permissions for an actor
    /// </summary>
    public async Task ClearActorCacheAsync(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor cannot be null or empty", nameof(actor));

        try
        {
            var groupsKey = GetGroupsCacheKey(actor);
            var permissionGroupKey = GetPermissionGroupKey(actor);

            await cache.RemoveAsync(groupsKey);
            await cache.RemoveGroupAsync(permissionGroupKey);

            logger.LogInformation("Cleared cache for actor {Actor}", actor);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing cache for actor {Actor}", actor);
            throw;
        }
    }
}