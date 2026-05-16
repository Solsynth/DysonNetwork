namespace DysonNetwork.Shared.Cache;

public interface ISharedCacheService
{
    Task<bool> SetData<T>(string key, T value, TimeSpan? expiry = null, string? typeName = null);

    Task<T?> GetData<T>(string key, string? typeName = null);

    Task<bool> SetFlagAsync(string key, TimeSpan? expiry = null);
    Task<bool> HasFlagAsync(string key);
    Task<bool> RemoveFlagAsync(string key);
}
