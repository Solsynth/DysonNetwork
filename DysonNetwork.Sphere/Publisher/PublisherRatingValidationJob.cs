using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherRatingValidationJob(AppDatabase db, ICacheService cache, ILogger<PublisherRatingValidationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting publisher rating update...");

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            const double baseRating = 100;

            var publisherIds = await db.PublisherRatingRecords
                .Where(r => !r.DeletedAt.HasValue)
                .Select(r => r.PublisherId)
                .Distinct()
                .ToListAsync();

            var processed = 0;
            const int batchSize = 100;

            for (var i = 0; i < publisherIds.Count; i += batchSize)
            {
                var batchIds = publisherIds.Skip(i).Take(batchSize).ToList();

                var records = await db.PublisherRatingRecords
                    .Where(r => batchIds.Contains(r.PublisherId) && !r.DeletedAt.HasValue)
                    .Select(r => new { r.PublisherId, r.Delta, r.CreatedAt, r.ExpiredAt })
                    .ToListAsync();

                var effectiveRatings = records
                    .GroupBy(r => r.PublisherId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(r =>
                        {
                            if (r.ExpiredAt.HasValue && r.ExpiredAt <= now)
                                return 0;
                            if (!r.ExpiredAt.HasValue)
                                return r.Delta;
                            var totalDuration = r.ExpiredAt.Value - r.CreatedAt;
                            if (totalDuration == Duration.Zero)
                                return r.Delta;
                            var elapsed = now - r.CreatedAt;
                            var remainingRatio = 1.0 - (elapsed.TotalSeconds / totalDuration.TotalSeconds);
                            return r.Delta * Math.Max(0, remainingRatio);
                        }) + baseRating
                    );

                foreach (var (publisherId, rating) in effectiveRatings)
                {
                    await db.Publishers
                        .Where(p => p.Id == publisherId)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.Rating, rating));
                }

                processed += batchIds.Count;
                logger.LogDebug("Processed {Processed}/{Total} publishers", processed, publisherIds.Count);
            }

            await cache.RemoveGroupAsync("publisher_rating:");
            logger.LogInformation("Publisher rating update completed. Updated {Count} publishers.", publisherIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during publisher rating update.");
        }
    }
}
