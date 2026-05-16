using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace DysonNetwork.Shared.Cache;

public sealed class CacheServiceRedis(
    IDistributedCache cache,
    IConnectionMultiplexer redis,
    ICacheSerializer serializer
) : ICacheService
{
    private const string GlobalKeyPrefix = "dyson:";
    private const string GroupKeyPrefix = GlobalKeyPrefix + "cg:";
    private const string KeyGroupPrefix = GlobalKeyPrefix + "cgk:";
    private const string LockKeyPrefix = GlobalKeyPrefix + "lock:";
    private const string Codec = "json";
    private const string CodecField = "codec";
    private const string DataField = "data";
    private const string TypeField = "type";
    private const string CachedAtField = "cached_at";
    private const string FlagValue = "1";

    private static string Normalize(string key) => $"{GlobalKeyPrefix}{key}";
    private static string GetKeyGroupIndex(string normalizedKey) => $"{KeyGroupPrefix}{normalizedKey}";

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
        var db = redis.GetDatabase();
        var keyGroupIndex = GetKeyGroupIndex(key);
        var groups = await db.SetMembersAsync(keyGroupIndex);
        if (groups.Length > 0)
        {
            var removeTasks = groups.Select(group => db.SetRemoveAsync(group.ToString(), key));
            await Task.WhenAll(removeTasks);
            await db.KeyDeleteAsync(keyGroupIndex);
        }
        await cache.RemoveAsync(key);
        return true;
    }

    public async Task AddToGroupAsync(string key, string group)
    {
        key = Normalize(key);
        var db = redis.GetDatabase();
        var groupKey = $"{GroupKeyPrefix}{group}";
        var keyGroupIndex = GetKeyGroupIndex(key);
        await db.SetAddAsync(groupKey, key);
        await db.SetAddAsync(keyGroupIndex, groupKey);
    }

    public async Task RemoveGroupAsync(string group)
    {
        var groupKey = $"{GroupKeyPrefix}{group}";
        var db = redis.GetDatabase();
        var keys = await db.SetMembersAsync(groupKey);
        if (keys.Length > 0)
        {
            foreach (var key in keys)
            {
                await cache.RemoveAsync(key.ToString());
                await db.SetRemoveAsync(GetKeyGroupIndex(key.ToString()), groupKey.ToString());
            }
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

    public async Task<bool> SetWithGroupsAsync<T>(string key, T value, IEnumerable<string>? groups = null, TimeSpan? expiry = null)
    {
        var result = await SetAsync(key, value, expiry);
        if (!result || groups == null)
            return result;
        var tasks = groups.Select(g => AddToGroupAsync(key, g));
        await Task.WhenAll(tasks);
        return true;
    }

    public async Task<bool> SetData<T>(string key, T value, TimeSpan? expiry = null, string? typeName = null)
    {
        var normalized = Normalize(key);
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(value);
        await db.HashSetAsync(normalized, new HashEntry[]
        {
            new(DataField, json),
            new(CodecField, Codec),
            new(TypeField, typeName ?? typeof(T).Name),
            new(CachedAtField, DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        });
        if (expiry.HasValue)
            await db.KeyExpireAsync(normalized, expiry.Value);
        return true;
    }

    public async Task<T?> GetData<T>(string key, string? typeName = null)
    {
        var normalized = Normalize(key);
        var db = redis.GetDatabase();
        var fields = await db.HashGetAllAsync(normalized);
        if (fields.Length == 0)
            return default;
        var map = fields.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
        if (!map.TryGetValue(CodecField, out var codec) || codec != Codec)
            return default;
        if (typeName is not null && map.TryGetValue(TypeField, out var storedType) &&
            !string.Equals(storedType, typeName, StringComparison.Ordinal))
            return default;
        if (!map.TryGetValue(DataField, out var data) || string.IsNullOrWhiteSpace(data))
            return default;
        return JsonSerializer.Deserialize<T>(data);
    }

    public async Task<bool> SetFlagAsync(string key, TimeSpan? expiry = null)
    {
        var normalized = Normalize(key);
        var db = redis.GetDatabase();
        await cache.SetStringAsync(normalized, FlagValue, expiry is null ? null : new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry });
        if (expiry.HasValue)
            await db.KeyExpireAsync(normalized, expiry.Value);
        return true;
    }

    public async Task<bool> HasFlagAsync(string key)
    {
        var normalized = Normalize(key);
        var value = await cache.GetStringAsync(normalized);
        return value == FlagValue;
    }

    public async Task<bool> RemoveFlagAsync(string key)
    {
        var normalized = Normalize(key);
        await cache.RemoveAsync(normalized);
        return true;
    }

    public async Task<IDistributedLock?> AcquireLockAsync(string resource, TimeSpan expiry, TimeSpan? waitTime = null, TimeSpan? retryInterval = null)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Resource cannot be null", nameof(resource));

        var lockKey = $"{LockKeyPrefix}{resource}";
        var db = redis.GetDatabase();
        var wait = waitTime ?? TimeSpan.Zero;
        var retry = retryInterval ?? TimeSpan.FromMilliseconds(100);
        var deadline = DateTimeOffset.UtcNow + wait;

        while (true)
        {
            var lockId = Guid.NewGuid().ToString("N");
            var acquired = await db.StringSetAsync(lockKey, lockId, expiry, When.NotExists);
            if (acquired)
                return new RedisDistributedLock(db, resource, lockKey, lockId);

            if (wait <= TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
                return null;

            await Task.Delay(retry);
        }
    }

    public async Task<bool> ExecuteWithLockAsync(string resource, Func<Task> action, TimeSpan expiry, TimeSpan? waitTime = null, TimeSpan? retryInterval = null)
    {
        await using var l = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);
        if (l is null)
            return false;
        await action();
        return true;
    }

    public async Task<(bool Acquired, T? Result)> ExecuteWithLockAsync<T>(string resource, Func<Task<T>> func, TimeSpan expiry, TimeSpan? waitTime = null, TimeSpan? retryInterval = null)
    {
        await using var l = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);
        if (l is null)
            return (false, default);
        var result = await func();
        return (true, result);
    }
}

public sealed class RedisDistributedLock(IDatabase db, string resource, string key, string lockId) : IDistributedLock
{
    public string Resource { get; } = resource;
    public string LockId => lockId;

    public async ValueTask ReleaseAsync()
    {
        await ReleaseCoreAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseCoreAsync();
        GC.SuppressFinalize(this);
    }

    private Task ReleaseCoreAsync()
    {
        const string lua = @"if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";
        return db.ScriptEvaluateAsync(lua, new RedisKey[] { key }, new RedisValue[] { lockId });
    }
}
