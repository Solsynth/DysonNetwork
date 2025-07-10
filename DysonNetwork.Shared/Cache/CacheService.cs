using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.JsonNet;
using StackExchange.Redis;

namespace DysonNetwork.Shared.Cache;

/// <summary>
/// Represents a distributed lock that can be used to synchronize access across multiple processes
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>
    /// The resource identifier this lock is protecting
    /// </summary>
    string Resource { get; }

    /// <summary>
    /// Unique identifier for this lock instance
    /// </summary>
    string LockId { get; }

    /// <summary>
    /// Extends the lock's expiration time
    /// </summary>
    Task<bool> ExtendAsync(TimeSpan timeSpan);

    /// <summary>
    /// Releases the lock immediately
    /// </summary>
    Task ReleaseAsync();
}

public interface ICacheService
{
    /// <summary>
    /// Sets a value in the cache with an optional expiration time
    /// </summary>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    /// <summary>
    /// Gets a value from the cache
    /// </summary>
    Task<T?> GetAsync<T>(string key);
    
    /// <summary>
    /// Get a value from the cache with the found status
    /// </summary>
    Task<(bool found, T? value)> GetAsyncWithStatus<T>(string key);

    /// <summary>
    /// Removes a specific key from the cache
    /// </summary>
    Task<bool> RemoveAsync(string key);

    /// <summary>
    /// Adds a key to a group for group-based operations
    /// </summary>
    Task AddToGroupAsync(string key, string group);

    /// <summary>
    /// Removes all keys associated with a specific group
    /// </summary>
    Task RemoveGroupAsync(string group);

    /// <summary>
    /// Gets all keys belonging to a specific group
    /// </summary>
    Task<IEnumerable<string>> GetGroupKeysAsync(string group);

    /// <summary>
    /// Helper method to set a value in cache and associate it with multiple groups in one operation
    /// </summary>
    /// <typeparam name="T">The type of value being cached</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="groups">Optional collection of group names to associate the key with</param>
    /// <param name="expiry">Optional expiration time for the cached item</param>
    /// <returns>True if the set operation was successful</returns>
    Task<bool> SetWithGroupsAsync<T>(string key, T value, IEnumerable<string>? groups = null, TimeSpan? expiry = null);

    /// <summary>
    /// Acquires a distributed lock on the specified resource
    /// </summary>
    /// <param name="resource">The resource identifier to lock</param>
    /// <param name="expiry">How long the lock should be held before automatically expiring</param>
    /// <param name="waitTime">How long to wait for the lock before giving up</param>
    /// <param name="retryInterval">How often to retry acquiring the lock during the wait time</param>
    /// <returns>A distributed lock instance if acquired, null otherwise</returns>
    Task<IDistributedLock?> AcquireLockAsync(string resource, TimeSpan expiry, TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null);

    /// <summary>
    /// Executes an action with a distributed lock, ensuring the lock is properly released afterwards
    /// </summary>
    /// <param name="resource">The resource identifier to lock</param>
    /// <param name="action">The action to execute while holding the lock</param>
    /// <param name="expiry">How long the lock should be held before automatically expiring</param>
    /// <param name="waitTime">How long to wait for the lock before giving up</param>
    /// <param name="retryInterval">How often to retry acquiring the lock during the wait time</param>
    /// <returns>True if the lock was acquired and the action was executed, false otherwise</returns>
    Task<bool> ExecuteWithLockAsync(string resource, Func<Task> action, TimeSpan expiry, TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null);

    /// <summary>
    /// Executes a function with a distributed lock, ensuring the lock is properly released afterwards
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="resource">The resource identifier to lock</param>
    /// <param name="func">The function to execute while holding the lock</param>
    /// <param name="expiry">How long the lock should be held before automatically expiring</param>
    /// <param name="waitTime">How long to wait for the lock before giving up</param>
    /// <param name="retryInterval">How often to retry acquiring the lock during the wait time</param>
    /// <returns>The result of the function if the lock was acquired, default(T) otherwise</returns>
    Task<(bool Acquired, T? Result)> ExecuteWithLockAsync<T>(string resource, Func<Task<T>> func, TimeSpan expiry,
        TimeSpan? waitTime = null, TimeSpan? retryInterval = null);
}

public class RedisDistributedLock : IDistributedLock
{
    private readonly IDatabase _database;
    private bool _disposed;

    public string Resource { get; }
    public string LockId { get; }

    internal RedisDistributedLock(IDatabase database, string resource, string lockId)
    {
        _database = database;
        Resource = resource;
        LockId = lockId;
    }

    public async Task<bool> ExtendAsync(TimeSpan timeSpan)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RedisDistributedLock));

        var script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('pexpire', KEYS[1], ARGV[2])
            else
                return 0
            end
        ";

        var result = await _database.ScriptEvaluateAsync(
            script,
            [$"{CacheServiceRedis.LockKeyPrefix}{Resource}"],
            [LockId, (long)timeSpan.TotalMilliseconds]
        );

        return (long)result! == 1;
    }

    public async Task ReleaseAsync()
    {
        if (_disposed)
            return;

        var script = @"
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end
        ";

        await _database.ScriptEvaluateAsync(
            script,
            [$"{CacheServiceRedis.LockKeyPrefix}{Resource}"],
            [LockId]
        );

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        GC.SuppressFinalize(this);
    }
}

public class CacheServiceRedis : ICacheService
{
    private readonly IDatabase _database;
    private readonly JsonSerializerSettings _serializerSettings;

    // Global prefix for all cache keys
    public const string GlobalKeyPrefix = "dyson:";

    // Using prefixes for different types of keys
    public const string GroupKeyPrefix = GlobalKeyPrefix + "cg:";
    public const string LockKeyPrefix = GlobalKeyPrefix + "lock:";

    public CacheServiceRedis(IConnectionMultiplexer redis)
    {
        var rds = redis ?? throw new ArgumentNullException(nameof(redis));
        _database = rds.GetDatabase();
        
        // Configure Newtonsoft.Json with proper NodaTime serialization
        _serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None
        };
        
        // Configure NodaTime serializers
        _serializerSettings.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        key = $"{GlobalKeyPrefix}{key}";
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var serializedValue = JsonConvert.SerializeObject(value, _serializerSettings);
        return await _database.StringSetAsync(key, serializedValue, expiry);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        key = $"{GlobalKeyPrefix}{key}";
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty)
            return default;

        // For NodaTime serialization, use the configured serializer settings
        return JsonConvert.DeserializeObject<T>(value!, _serializerSettings);
    }

    public async Task<(bool found, T? value)> GetAsyncWithStatus<T>(string key)
    {
        key = $"{GlobalKeyPrefix}{key}";
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty)
            return (false, default);

        // For NodaTime serialization, use the configured serializer settings
        return (true, JsonConvert.DeserializeObject<T>(value!, _serializerSettings));
    }

    public async Task<bool> RemoveAsync(string key)
    {
        key = $"{GlobalKeyPrefix}{key}";
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        // Before removing the key, find all groups it belongs to and remove it from them
        var script = @"
            local groups = redis.call('KEYS', ARGV[1])
            for _, group in ipairs(groups) do
                redis.call('SREM', group, ARGV[2])
            end
            return redis.call('DEL', ARGV[2])
        ";

        var result = await _database.ScriptEvaluateAsync(
            script,
            values: [$"{GroupKeyPrefix}*", key]
        );

        return (long)result! > 0;
    }

    public async Task AddToGroupAsync(string key, string group)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException(@"Key cannot be null or empty.", nameof(key));

        if (string.IsNullOrEmpty(group))
            throw new ArgumentException(@"Group cannot be null or empty.", nameof(group));

        var groupKey = $"{GroupKeyPrefix}{group}";
        key = $"{GlobalKeyPrefix}{key}";
        await _database.SetAddAsync(groupKey, key);
    }

    public async Task RemoveGroupAsync(string group)
    {
        if (string.IsNullOrEmpty(group))
            throw new ArgumentException(@"Group cannot be null or empty.", nameof(group));

        var groupKey = $"{GroupKeyPrefix}{group}";

        // Get all keys in the group
        var keys = await _database.SetMembersAsync(groupKey);

        if (keys.Length > 0)
        {
            // Delete all the keys
            var keysTasks = keys.Select(key => _database.KeyDeleteAsync(key.ToString()));
            await Task.WhenAll(keysTasks);
        }

        // Delete the group itself
        await _database.KeyDeleteAsync(groupKey);
    }

    public async Task<IEnumerable<string>> GetGroupKeysAsync(string group)
    {
        if (string.IsNullOrEmpty(group))
            throw new ArgumentException(@"Group cannot be null or empty.", nameof(group));

        var groupKey = $"{GroupKeyPrefix}{group}";
        var members = await _database.SetMembersAsync(groupKey);

        return members.Select(m => m.ToString());
    }

    public async Task<bool> SetWithGroupsAsync<T>(string key, T value, IEnumerable<string>? groups = null,
        TimeSpan? expiry = null)
    {
        // First, set the value in the cache
        var setResult = await SetAsync(key, value, expiry);

        // If successful and there are groups to associate, add the key to each group
        if (!setResult || groups == null) return setResult;
        var groupsArray = groups.Where(g => !string.IsNullOrEmpty(g)).ToArray();
        if (groupsArray.Length <= 0) return setResult;
        var tasks = groupsArray.Select(group => AddToGroupAsync(key, group));
        await Task.WhenAll(tasks);

        return setResult;
    }

    public async Task<IDistributedLock?> AcquireLockAsync(string resource, TimeSpan expiry, TimeSpan? waitTime = null,
        TimeSpan? retryInterval = null)
    {
        if (string.IsNullOrEmpty(resource))
            throw new ArgumentException("Resource cannot be null or empty", nameof(resource));

        var lockKey = $"{LockKeyPrefix}{resource}";
        var lockId = Guid.NewGuid().ToString("N");
        var waitTimeSpan = waitTime ?? TimeSpan.Zero;
        var retryIntervalSpan = retryInterval ?? TimeSpan.FromMilliseconds(100);

        var startTime = DateTime.UtcNow;
        var acquired = false;

        // Try to acquire the lock, retry until waitTime is exceeded
        while (!acquired && (DateTime.UtcNow - startTime) < waitTimeSpan)
        {
            acquired = await _database.StringSetAsync(lockKey, lockId, expiry, When.NotExists);

            if (!acquired)
            {
                await Task.Delay(retryIntervalSpan);
            }
        }

        if (!acquired)
        {
            return null; // Could not acquire the lock within the wait time
        }

        return new RedisDistributedLock(_database, resource, lockId);
    }

    public async Task<bool> ExecuteWithLockAsync(string resource, Func<Task> action, TimeSpan expiry,
        TimeSpan? waitTime = null, TimeSpan? retryInterval = null)
    {
        await using var lockObj = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);

        if (lockObj == null)
            return false; // Could not acquire the lock

        await action();
        return true;
    }

    public async Task<(bool Acquired, T? Result)> ExecuteWithLockAsync<T>(string resource, Func<Task<T>> func,
        TimeSpan expiry, TimeSpan? waitTime = null, TimeSpan? retryInterval = null)
    {
        await using var lockObj = await AcquireLockAsync(resource, expiry, waitTime, retryInterval);

        if (lockObj == null)
            return (false, default); // Could not acquire the lock

        var result = await func();
        return (true, result);
    }
}