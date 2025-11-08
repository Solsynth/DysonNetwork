using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Cache;
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

    private static string GetPermissionCacheKey(string actor, string area, string key) =>
        PermissionCacheKeyPrefix + actor + ":" + area + ":" + key;

    private static string GetGroupsCacheKey(string actor) =>
        PermissionGroupCacheKeyPrefix + actor;

    private static string GetPermissionGroupKey(string actor) =>
        PermissionGroupPrefix + actor;

    public async Task<bool> HasPermissionAsync(string actor, string area, string key)
    {
        var value = await GetPermissionAsync<bool>(actor, area, key);
        return value;
    }

    public async Task<T?> GetPermissionAsync<T>(string actor, string area, string key)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor cannot be null or empty", nameof(actor));
        if (string.IsNullOrWhiteSpace(area))
            throw new ArgumentException("Area cannot be null or empty", nameof(area));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var cacheKey = GetPermissionCacheKey(actor, area, key);

        try
        {
            var (hit, cachedValue) = await cache.GetAsyncWithStatus<T>(cacheKey);
            if (hit)
            {
                logger.LogDebug("Permission cache hit for {Actor}:{Area}:{Key}", actor, area, key);
                return cachedValue;
            }

            var now = SystemClock.Instance.GetCurrentInstant();
            var groupsId = await GetOrCacheUserGroupsAsync(actor, now);

            var permission = await FindPermissionNodeAsync(actor, area, key, groupsId, now);
            var result = permission != null ? DeserializePermissionValue<T>(permission.Value) : default;

            await cache.SetWithGroupsAsync(cacheKey, result,
                [GetPermissionGroupKey(actor)],
                _options.CacheExpiration);

            logger.LogDebug("Permission resolved for {Actor}:{Area}:{Key} = {Result}",
                actor, area, key, result != null);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving permission for {Actor}:{Area}:{Key}", actor, area, key);
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

    private async Task<SnPermissionNode?> FindPermissionNodeAsync(string actor, string area, string key,
        List<Guid> groupsId, Instant now)
    {
        // First try exact match (highest priority)
        var exactMatch = await db.PermissionNodes
            .Where(n => (n.GroupId == null && n.Actor == actor) ||
                        (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
            .Where(n => n.Key == key && n.Area == area)
            .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
            .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
            .FirstOrDefaultAsync();

        if (exactMatch != null)
        {
            return exactMatch;
        }

        // If no exact match and wildcards are enabled, try wildcard matches
        if (!_options.EnableWildcardMatching)
        {
            return null;
        }

        var wildcardMatches = await db.PermissionNodes
            .Where(n => (n.GroupId == null && n.Actor == actor) ||
                        (n.GroupId != null && groupsId.Contains(n.GroupId.Value)))
            .Where(n => (n.Key.Contains("*") || n.Area.Contains("*")))
            .Where(n => n.ExpiredAt == null || n.ExpiredAt > now)
            .Where(n => n.AffectedAt == null || n.AffectedAt <= now)
            .Take(_options.MaxWildcardMatches)
            .ToListAsync();

        // Find the best wildcard match
        SnPermissionNode? bestMatch = null;
        var bestMatchScore = -1;

        foreach (var node in wildcardMatches)
        {
            var score = CalculateWildcardMatchScore(node.Area, node.Key, area, key);
            if (score > bestMatchScore)
            {
                bestMatch = node;
                bestMatchScore = score;
            }
        }

        if (bestMatch != null)
        {
            logger.LogDebug("Found wildcard permission match: {NodeArea}:{NodeKey} for {Area}:{Key}",
                bestMatch.Area, bestMatch.Key, area, key);
        }

        return bestMatch;
    }

    private static int CalculateWildcardMatchScore(string nodeArea, string nodeKey, string targetArea, string targetKey)
    {
        // Calculate how well the wildcard pattern matches
        // Higher score = better match
        var areaScore = CalculatePatternMatchScore(nodeArea, targetArea);
        var keyScore = CalculatePatternMatchScore(nodeKey, targetKey);

        // Perfect match gets highest score
        if (areaScore == int.MaxValue && keyScore == int.MaxValue)
            return int.MaxValue;

        // Prefer area matches over key matches, more specific patterns over general ones
        return (areaScore * 1000) + keyScore;
    }

    private static int CalculatePatternMatchScore(string pattern, string target)
    {
        if (pattern == target)
            return int.MaxValue; // Exact match

        if (!pattern.Contains("*"))
            return -1; // No wildcard, not a match

        // Simple wildcard matching: * matches any sequence of characters
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (regex.IsMatch(target))
        {
            // Score based on specificity (shorter patterns are less specific)
            var wildcardCount = pattern.Count(c => c == '*');
            var length = pattern.Length;
            return Math.Max(1, 1000 - (wildcardCount * 100) - length);
        }

        return -1; // No match
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
            Value = SerializePermissionValue(value),
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
            Value = SerializePermissionValue(value),
            ExpiredAt = expiredAt,
            AffectedAt = affectedAt,
            Group = group,
            GroupId = group.Id
        };

        db.PermissionNodes.Add(node);
        await db.SaveChangesAsync();

        // Invalidate related caches
        await InvalidatePermissionCacheAsync(actor, area, key);
        await cache.RemoveAsync(GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(GetPermissionGroupKey(actor));

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
        await cache.RemoveAsync(GetGroupsCacheKey(actor));
        await cache.RemoveGroupAsync(GetPermissionGroupKey(actor));
    }

    private async Task InvalidatePermissionCacheAsync(string actor, string area, string key)
    {
        var cacheKey = GetPermissionCacheKey(actor, area, key);
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

    public static SnPermissionNode NewPermissionNode<T>(string actor, string area, string key, T value)
    {
        return new SnPermissionNode
        {
            Actor = actor,
            Area = area,
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
                .OrderBy(n => n.Area)
                .ThenBy(n => n.Key)
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
                .OrderBy(n => n.Area)
                .ThenBy(n => n.Key)
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
