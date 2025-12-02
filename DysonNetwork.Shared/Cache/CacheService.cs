using Microsoft.Extensions.Caching.Distributed;
using RedLockNet;
using StackExchange.Redis;

namespace DysonNetwork.Shared.Cache;

public class CacheServiceRedis(
    IDistributedCache cache,
    IConnectionMultiplexer redis,
    ICacheSerializer serializer,
    IDistributedLockFactory lockFactory
)
    : ICacheService
{
    private const string GlobalKeyPrefix = "dyson:";
    private const string GroupKeyPrefix = GlobalKeyPrefix + "cg:";
    private const string LockKeyPrefix = GlobalKeyPrefix + "lock:";

    private static string Normalize(string key) => $"{GlobalKeyPrefix}{key}";

    // -----------------------------------------------------
    // BASIC OPERATIONS
    // -----------------------------------------------------

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        key = Normalize(key);

        var json = serializer.Serialize(value);

        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);

        await cache.SetStringAsync(key, json, options);
        return true;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        key = Normalize(key);

        var json = await cache.GetStringAsync(key);
        if (json is null)
            return default;

        return serializer.Deserialize<T>(json);
    }

    public async Task<(bool found, T? value)> GetAsyncWithStatus<T>(string key)
    {
        key = Normalize(key);

        var json = await cache.GetStringAsync(key);
        if (json is null)
            return (false, default);

        return (true, serializer.Deserialize<T>(json));
    }

    public async Task<bool> RemoveAsync(string key)
    {
        key = Normalize(key);

        // Remove key from all groups
        var db = redis.GetDatabase();

        var groupPattern = $"{GroupKeyPrefix}*";
        var server = redis.GetServers().First();

        var groups = server.Keys(pattern: groupPattern);
        foreach (var group in groups)
        {
            await db.SetRemoveAsync(group, key);
        }

        await cache.RemoveAsync(key);
        return true;
    }

    // -----------------------------------------------------
    // GROUP OPERATIONS
    // -----------------------------------------------------

    public async Task AddToGroupAsync(string key, string group)
    {
        key = Normalize(key);
        var db = redis.GetDatabase();
        var groupKey = $"{GroupKeyPrefix}{group}";
        await db.SetAddAsync(groupKey, key);
    }

    public async Task RemoveGroupAsync(string group)
    {
        var groupKey = $"{GroupKeyPrefix}{group}";
        var db = redis.GetDatabase();

        var keys = await db.SetMembersAsync(groupKey);

        if (keys.Length > 0)
        {
            foreach (var key in keys)
                await cache.RemoveAsync(key.ToString());
        }

        await db.KeyDeleteAsync(groupKey);
    }

    public async Task<IEnumerable<string>> GetGroupKeysAsync(string group)
    {
        var groupKey = $"{GroupKeyPrefix}{group}";
        var db = redis.GetDatabase();
        var members = await db.SetMembersAsync(groupKey);
        return members.Select(x => x.ToString());
    }

    public async Task<bool> SetWithGroupsAsync<T>(
        string key,
        T value,
        IEnumerable<string>? groups = null,
        TimeSpan? expiry = null)
    {
        var result = await SetAsync(key, value, expiry);
        if (!result || groups == null)
            return result;

        var tasks = groups.Select(g => AddToGroupAsync(key, g));
        await Task.WhenAll(tasks);
        return true;
    }

    // -----------------------------------------------------
    // DISTRIBUTED LOCK (RedLock wrapper)
    // -----------------------------------------------------

    private readonly TimeSpan _defaultRetry = TimeSpan.FromMilliseconds(100);

    public async Task<IDistributedLock?> AcquireLockAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource cannot be null", nameof(resource));

        var lockKey = $"{LockKeyPrefix}{resource}";
        var redlock = await lockFactory.CreateLockAsync(
            lockKey,
            expiry,
            waitTime ?? TimeSpan.Zero,
            retryInterval ?? _defaultRetry
        );

        return !redlock.IsAcquired ? null : new RedLockAdapter(redlock, resource);
    }

    public async Task<bool> ExecuteWithLockAsync(
        string resource,
        Func<Task> action,
        TimeSpan expiry,
        TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null)
    {
        await using var l = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);
        if (l is null)
            return false;

        await action();
        return true;
    }

    public async Task<(bool Acquired, T? Result)> ExecuteWithLockAsync<T>(
        string resource,
        Func<Task<T>> func,
        TimeSpan expiry,
        TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null)
    {
        await using var l = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);
        if (l is null)
            return (false, default);

        var result = await func();
        return (true, result);
    }
}

public class RedLockAdapter(IRedLock inner, string resource) : IDistributedLock
{
    public string Resource { get; } = resource;
    public string LockId => inner.LockId;

    public ValueTask ReleaseAsync() => inner.DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}