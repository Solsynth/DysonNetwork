namespace DysonNetwork.Shared.Cache;

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
