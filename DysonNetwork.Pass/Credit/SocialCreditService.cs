using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditService(AppDatabase db, ICacheService cache)
{
    private const string CacheKeyPrefix = "credits:";
    private const string CacheGroupPrefix = "account:";
    private static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<SnSocialCreditRecord> AddRecord(
        string reasonType,
        string reason,
        double delta,
        Guid accountId,
        Instant? expiredAt
    )
    {
        var record = new SnSocialCreditRecord
        {
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta,
            AccountId = accountId,
            ExpiredAt = expiredAt
        };
        db.SocialCreditRecords.Add(record);
        await db.SaveChangesAsync();

        await cache.RemoveGroupAsync($"{CacheGroupPrefix}{accountId}");

        return record;
    }

    public Task InvalidateCache()
    {
        return cache.RemoveGroupAsync(CacheKeyPrefix);
    }

    private const double BaseSocialCredit = 100;

    public async Task<double> GetSocialCredit(Guid accountId)
    {
        var cacheKey = $"{CacheKeyPrefix}{accountId}";
        var cached = await cache.GetAsync<double?>(cacheKey);
        if (cached.HasValue) return cached.Value;

        var now = SystemClock.Instance.GetCurrentInstant();

        var credits = await db.SocialCreditRecords
            .Where(r => r.AccountId == accountId && !r.DeletedAt.HasValue)
            .ToListAsync();

        var total = credits.Sum(r => r.GetEffectiveDelta(now));
        total += BaseSocialCredit;

        await cache.SetWithGroupsAsync(cacheKey, total, [$"{CacheGroupPrefix}{accountId}"], MinCacheDuration);

        return total;
    }
}