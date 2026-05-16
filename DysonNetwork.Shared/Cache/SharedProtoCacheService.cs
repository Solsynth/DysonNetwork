using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;

namespace DysonNetwork.Shared.Cache;

public sealed class SharedCacheService(
    IDistributedCache cache,
    IConnectionMultiplexer redis
) : ISharedCacheService
{
    private const string GlobalKeyPrefix = "dyson:";
    private const string Codec = "json";
    private const string CodecField = "codec";
    private const string DataField = "data";
    private const string TypeField = "type";
    private const string CachedAtField = "cached_at";
    private const string FlagValue = "1";

    private static string Normalize(string key) => $"{GlobalKeyPrefix}{key}";

    public async Task<bool> SetData<T>(string key, T value, TimeSpan? expiry = null, string? typeName = null)
    {
        var normalized = Normalize(key);
        var json = JsonSerializer.Serialize(value);
        var db = redis.GetDatabase();
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
}
