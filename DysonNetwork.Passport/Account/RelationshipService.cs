using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DysonNetwork.Passport.Account;

/// <summary>
/// Relationship reads. The <c>account_relationships</c> table is owned by
/// Stargate now, so this service is a thin client over the
/// <see cref="StargateProfileClient"/> instead of talking to Passport's own
/// database. The six <c>DyProfileService</c> relationship RPCs carry the same
/// semantics the local queries used to implement.
///
/// <para>Stargate is a runtime dependency of every friend/block gate in
/// Passport, so an unreachable Stargate must not turn presence, meets, nearby,
/// NFC and calendar reads into 500s. When the RPC fails at the transport level
/// the service degrades to the relationship cache Stargate publishes
/// (<c>accounts:friends:</c> and friends) and, failing that, to the
/// empty/false default. That is the same fail-open policy
/// <see cref="DysonNetwork.Shared.Registry.RemoteAccountService"/> already
/// applies to the sibling services: "not a friend" and "not blocked" hide
/// nothing the viewer could not otherwise see, while keeping reads available
/// during a Stargate outage.</para>
/// </summary>
public class RelationshipService(
    StargateProfileClient stargateProfiles,
    ICacheService cache,
    IConnectionMultiplexer rawRedis,
    ICacheSerializer serializer,
    IOptions<RelationshipFallbackOptions> fallbackOptions,
    ILogger<RelationshipService> logger
)
{
    private const string UserFriendsCacheKeyPrefix = "accounts:friends:";
    private const string UserBlockedCacheKeyPrefix = "accounts:blocked:";
    private const string UserMutedCacheKeyPrefix = "accounts:muted:";
    private const string UserCloseFriendsCacheKeyPrefix = "accounts:close_friends:";
    private const string GlobalCacheKeyPrefix = "dyson:";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    // One warning per window keeps a Stargate outage from flooding the log on
    // every status read while still surfacing the degradation.
    private static readonly TimeSpan WarningThrottle = TimeSpan.FromSeconds(30);
    private static long _lastDegradedWarningTicks;

    private readonly RelationshipFallbackOptions _fallback = fallbackOptions.Value;

    public async Task<bool> HasExistingRelationship(Guid accountId, Guid relatedId)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");
        if (accountId == relatedId)
            return false; // Prevent self-relationships

        return await DegradeAsync(
            "HasExistingRelationship",
            async () =>
            {
                var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
                {
                    AccountId = accountId.ToString(),
                    RelatedId = relatedId.ToString()
                });
                return response.Value;
            },
            // No cache key carries the "any status, either direction" answer,
            // so an outage reports no relationship rather than inventing one.
            () => Task.FromResult(false)
        );
    }

    public async Task<bool> HasRelationshipWithStatus(Guid accountId, Guid relatedId,
        RelationshipStatus status = RelationshipStatus.Friends)
    {
        if (accountId == Guid.Empty || relatedId == Guid.Empty)
            throw new ArgumentException("Account IDs cannot be empty.");

        return await DegradeAsync(
            "HasRelationshipWithStatus",
            async () =>
            {
                var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
                {
                    AccountId = accountId.ToString(),
                    RelatedId = relatedId.ToString(),
                    Status = (int)status
                });
                return response.Value;
            },
            async () => status switch
            {
                RelationshipStatus.Blocked => await IsBlockedInCacheAsync(accountId, relatedId),
                RelationshipStatus.CloseFriend =>
                    (await ReadCachedIdsAsync(UserCloseFriendsCacheKeyPrefix, accountId, false)).Contains(relatedId),
                RelationshipStatus.Muted =>
                    (await ReadCachedIdsAsync(UserMutedCacheKeyPrefix, accountId, false)).Contains(relatedId),
                RelationshipStatus.Friends =>
                    (await ReadCachedIdsAsync(UserFriendsCacheKeyPrefix, accountId, false)).Contains(relatedId),
                _ => false
            }
        );
    }

    public async Task<bool> IsBlockedEitherDirection(Guid userId, Guid otherId)
    {
        if (userId == Guid.Empty || otherId == Guid.Empty)
            return false;
        if (userId == otherId)
            return false;

        return await DegradeAsync(
            "IsBlockedEitherDirection",
            async () =>
            {
                var response = await stargateProfiles.HasRelationshipAsync(new DyGetRelationshipRequest
                {
                    AccountId = userId.ToString(),
                    RelatedId = otherId.ToString(),
                    Status = (int)RelationshipStatus.Blocked,
                    EitherDirection = true
                });
                return response.Value;
            },
            () => IsBlockedInCacheAsync(userId, otherId)
        );
    }

    public async Task<List<Guid>> ListAccountFriends(SnAccount account, bool isRelated = false)
    {
        return await ListAccountFriends(account.Id, isRelated);
    }

    public async Task<List<Guid>> ListAccountFriends(Guid accountId, bool isRelated = false)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated,
            request => stargateProfiles.ListFriendsAsync(request).ResponseAsync,
            UserFriendsCacheKeyPrefix
        );
    }

    public async Task<List<Guid>> ListAccountBlocked(SnAccount account, bool isRelated = false)
    {
        return await ListAccountBlocked(account.Id, isRelated);
    }

    public async Task<List<Guid>> ListAccountBlocked(Guid accountId, bool isRelated = false)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated,
            request => stargateProfiles.ListBlockedAsync(request).ResponseAsync,
            UserBlockedCacheKeyPrefix
        );
    }

    /// <remarks>
    /// Each direction degrades on its own, so an outage yields the cached
    /// outgoing and incoming block lists rather than one merged cache entry.
    /// </remarks>
    public async Task<List<Guid>> ListAllBlockedAccountIds(Guid accountId)
    {
        var outgoing = await ListAccountBlocked(accountId);
        var incoming = await ListAccountBlocked(accountId, true);
        return outgoing.Concat(incoming).Distinct().ToList();
    }

    public async Task<List<Guid>> ListAccountMuted(Guid accountId)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated: false,
            request => stargateProfiles.ListMutedAsync(request).ResponseAsync,
            UserMutedCacheKeyPrefix
        );
    }

    public async Task<List<Guid>> ListCloseFriends(Guid accountId)
    {
        return await ListRelationshipsAsync(
            accountId,
            isRelated: false,
            request => stargateProfiles.ListCloseFriendsAsync(request).ResponseAsync,
            UserCloseFriendsCacheKeyPrefix
        );
    }

    public async Task<bool> IsCloseFriend(Guid userId, Guid otherId)
    {
        if (userId == Guid.Empty || otherId == Guid.Empty)
            return false;
        if (userId == otherId)
            return false;

        return await HasRelationshipWithStatus(userId, otherId, RelationshipStatus.CloseFriend);
    }

    /// <summary>
    /// Calls one of the relationship list RPCs. <paramref name="isRelated"/> selects
    /// the reverse direction (rows where the account is the related side).
    /// </summary>
    private async Task<List<Guid>> ListRelationshipsAsync(
        Guid accountId,
        bool isRelated,
        Func<DyListRelationshipSimpleRequest, Task<DyListRelationshipSimpleResponse>> call,
        string cacheKeyPrefix
    )
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID cannot be empty.");

        var request = new DyListRelationshipSimpleRequest();
        if (isRelated)
            request.RelatedId = accountId.ToString();
        else
            request.AccountId = accountId.ToString();

        return await DegradeAsync(
            $"List({cacheKeyPrefix})",
            async () =>
            {
                var response = await call(request);
                return ParseIds(response.AccountsId);
            },
            () => ReadCachedIdsAsync(cacheKeyPrefix, accountId, isRelated)
        );
    }

    /// <summary>
    /// Runs a relationship read, falling back when Stargate is unreachable.
    /// Only transport-level failures degrade; a bug in Stargate (Internal) or a
    /// cancelled request still surfaces.
    /// </summary>
    private async Task<T> DegradeAsync<T>(string operation, Func<Task<T>> primary, Func<Task<T>> fallback)
    {
        try
        {
            return await primary();
        }
        catch (RpcException ex) when (IsUnavailable(ex))
        {
            if (!_fallback.Enabled)
                throw;

            WarnDegraded(operation, ex);
            return await fallback();
        }
    }

    private static bool IsUnavailable(RpcException ex) => ex.StatusCode
        is StatusCode.Unavailable
        or StatusCode.DeadlineExceeded
        or StatusCode.Unimplemented;

    private void WarnDegraded(string operation, RpcException ex)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDegradedWarningTicks);
        if (now - last < (long)WarningThrottle.TotalMilliseconds)
            return;
        if (Interlocked.CompareExchange(ref _lastDegradedWarningTicks, now, last) != last)
            return;

        logger.LogWarning(
            ex,
            "Relationship read {Operation} degraded to cache/default: Stargate returned {StatusCode}",
            operation,
            ex.StatusCode
        );
    }

    private async Task<bool> IsBlockedInCacheAsync(Guid userId, Guid otherId)
    {
        var (smaller, larger) = userId.CompareTo(otherId) < 0
            ? (userId, otherId)
            : (otherId, userId);

        var either = await ReadCachedValueAsync<bool>(
            $"{UserBlockedCacheKeyPrefix}either:{smaller}:{larger}");
        if (either.Found)
            return either.Value;

        return (await ReadCachedIdsAsync(UserBlockedCacheKeyPrefix, userId, false)).Contains(otherId)
            || (await ReadCachedIdsAsync(UserBlockedCacheKeyPrefix, otherId, false)).Contains(userId);
    }

    /// <summary>
    /// Reads a relationship list from the shared Redis cache.
    ///
    /// <para>The two writers disagree on the Redis type, not just the payload:
    /// the C# <see cref="ICacheService"/> stores a hash envelope
    /// (<c>absexp</c>/<c>sldexp</c>/<c>data</c>, the last base64 encoded), while
    /// Stargate's Go cache writes a plain string holding the JSON. Reading this
    /// path only through <see cref="ICacheService"/> therefore cannot see a
    /// Stargate-written entry at all (it throws WRONGTYPE), so the raw
    /// multiplexer is consulted as well.</para>
    /// </summary>
    private async Task<List<Guid>> ReadCachedIdsAsync(string prefix, Guid accountId, bool isRelated)
    {
        foreach (var key in CandidateKeys(prefix, accountId, isRelated))
        {
            var cached = await ReadCachedValueAsync<List<string>>(key);
            if (cached.Found && cached.Value is not null)
                return ParseIds(cached.Value);
        }

        return [];
    }

    /// <summary>
    /// The suffixed key is the current shape both writers use; the bare key is
    /// the pre-cutover shape the old C# writer also purged.
    /// </summary>
    private static IEnumerable<string> CandidateKeys(string prefix, Guid accountId, bool isRelated)
    {
        yield return $"{prefix}{accountId}:{(isRelated ? "True" : "False")}";
        yield return $"{prefix}{accountId}";
    }

    /// <summary>
    /// Reads one shared-cache entry in whichever encoding it was written.
    /// Every failure resolves to "not found": this path is best-effort and must
    /// never turn a Stargate outage into a 500.
    /// </summary>
    private async Task<(bool Found, T? Value)> ReadCachedValueAsync<T>(string key)
    {
        var normalized = GlobalCacheKeyPrefix + key;

        // C# writer (and this process): hash envelope via ICacheService.
        try
        {
            var (found, value) = await cache.GetAsyncWithStatus<T>(key);
            if (found)
                return (true, value);
        }
        catch (Exception ex) when (ex is RedisServerException or RedisConnectionException)
        {
            // WRONGTYPE means the Go writer owns this key as a plain string.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Relationship fallback cache read failed for {Key}", key);
            return (false, default);
        }

        // Go writer: plain string holding the JSON payload.
        try
        {
            var raw = await rawRedis.GetDatabase().StringGetAsync(normalized);
            if (raw.IsNullOrEmpty)
                return (false, default);

            var value = serializer.Deserialize<T>(raw.ToString());
            return (value is not null, value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Relationship fallback raw cache read failed for {Key}", key);
            return (false, default);
        }
    }

    private static List<Guid> ParseIds(IEnumerable<string> ids)
    {
        var parsed = new List<Guid>();
        foreach (var id in ids)
        {
            if (Guid.TryParse(id, out var value))
                parsed.Add(value);
        }

        return parsed;
    }

    /// <summary>
    /// Kept for parity with the pre-migration cache TTL. Stargate owns writes
    /// to these keys; Passport only reads them in the degraded path.
    /// </summary>
    internal static TimeSpan CachedRelationshipTtl => CacheExpiration;
}

/// <summary>
/// Controls what a relationship read does when Stargate is unreachable.
/// </summary>
public class RelationshipFallbackOptions
{
    /// <summary>
    /// Degrade to the shared relationship cache (and then to the empty/false
    /// default) instead of failing the request. Disable to make a Stargate
    /// outage fail loudly, which is useful when debugging a topology change.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
