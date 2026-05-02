using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherRatingService(AppDatabase db, ICacheService cache)
{
    private const string CacheKeyPrefix = "publisher_rating:";
    private const string CacheGroupPrefix = "publisher:";
    private static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(5);

    private const double BaseRating = 100;

    public async Task<SnPublisherRatingRecord> AddRecord(
        string reasonType,
        string reason,
        double delta,
        Guid publisherId
    )
    {
        var record = new SnPublisherRatingRecord
        {
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta,
            PublisherId = publisherId
        };
        db.PublisherRatingRecords.Add(record);
        await db.SaveChangesAsync();

        await cache.RemoveGroupAsync($"{CacheGroupPrefix}{publisherId}");

        var total = await db.PublisherRatingRecords
            .Where(r => r.PublisherId == publisherId && !r.DeletedAt.HasValue)
            .SumAsync(r => r.Delta) + BaseRating;

        await db.Publishers
            .Where(p => p.Id == publisherId)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Rating, total));

        await cache.RemoveAsync("publisher_leaderboard");

        return record;
    }

    public async Task<double> GetRating(Guid publisherId)
    {
        var cacheKey = $"{CacheKeyPrefix}{publisherId}";
        var cached = await cache.GetAsync<double?>(cacheKey);
        if (cached.HasValue) return cached.Value;

        var total = await db.PublisherRatingRecords
            .Where(r => r.PublisherId == publisherId && !r.DeletedAt.HasValue)
            .SumAsync(r => r.Delta) + BaseRating;

        await cache.SetWithGroupsAsync(cacheKey, total, [$"{CacheGroupPrefix}{publisherId}"], MinCacheDuration);

        return total;
    }

    public async Task<List<SnPublisherRatingRecord>> GetRatingHistory(
        Guid publisherId,
        int take = 20,
        int offset = 0
    )
    {
        return await db.PublisherRatingRecords
            .Where(r => r.PublisherId == publisherId && !r.DeletedAt.HasValue)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetRatingHistoryCount(Guid publisherId)
    {
        return await db.PublisherRatingRecords
            .Where(r => r.PublisherId == publisherId && !r.DeletedAt.HasValue)
            .CountAsync();
    }

    public Task InvalidateCache()
    {
        return cache.RemoveGroupAsync(CacheKeyPrefix);
    }
}
