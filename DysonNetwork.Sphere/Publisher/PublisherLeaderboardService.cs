using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherLeaderboardService(AppDatabase db, ICacheService cache)
{
    private const string CacheKey = "publisher_leaderboard";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public class LeaderboardEntry
    {
        public Guid PublisherId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Nick { get; set; } = string.Empty;
        public SnCloudFileReferenceObject? Picture { get; set; }
        public double Rating { get; set; }
        public int Rank { get; set; }
        public double Percentile { get; set; }
        public string Grade { get; set; } = string.Empty;
    }

    public class RatingOverview
    {
        public double Rating { get; set; }
        public int Rank { get; set; }
        public int TotalPublishers { get; set; }
        public double Percentile { get; set; }
        public string Grade { get; set; } = string.Empty;
    }

    private class CachedLeaderboard
    {
        public List<LeaderboardEntry> Entries { get; set; } = [];
        public Instant CachedAt { get; set; }
    }

    public async Task<List<LeaderboardEntry>> GetLeaderboard(int take = 20, int offset = 0)
    {
        var entries = await GetCachedEntries();
        return entries.Skip(offset).Take(take).ToList();
    }

    public async Task<int> GetTotalPublishers()
    {
        var entries = await GetCachedEntries();
        return entries.Count;
    }

    public async Task<RatingOverview?> GetOverview(Guid publisherId)
    {
        var entries = await GetCachedEntries();
        var entry = entries.FirstOrDefault(e => e.PublisherId == publisherId);
        if (entry is null) return null;

        return new RatingOverview
        {
            Rating = entry.Rating,
            Rank = entry.Rank,
            TotalPublishers = entries.Count,
            Percentile = entry.Percentile,
            Grade = entry.Grade
        };
    }

    public async Task<RatingOverview?> GetOverviewByName(string name)
    {
        var entries = await GetCachedEntries();
        var entry = entries.FirstOrDefault(e => e.Name == name);
        if (entry is null) return null;

        return new RatingOverview
        {
            Rating = entry.Rating,
            Rank = entry.Rank,
            TotalPublishers = entries.Count,
            Percentile = entry.Percentile,
            Grade = entry.Grade
        };
    }

    public Task InvalidateCache()
    {
        return cache.RemoveAsync(CacheKey);
    }

    private async Task<List<LeaderboardEntry>> GetCachedEntries()
    {
        var cached = await cache.GetAsync<CachedLeaderboard>(CacheKey);
        if (cached is not null) return cached.Entries;

        var entries = await BuildLeaderboard();
        await cache.SetAsync(CacheKey, new CachedLeaderboard
        {
            Entries = entries,
            CachedAt = SystemClock.Instance.GetCurrentInstant()
        }, CacheDuration);

        return entries;
    }

    private async Task<List<LeaderboardEntry>> BuildLeaderboard()
    {
        var publishers = await db.Publishers
            .Where(p => !p.DeletedAt.HasValue)
            .Select(p => new { p.Id, p.Name, p.Nick, p.Picture, p.Rating })
            .OrderByDescending(p => p.Rating)
            .ToListAsync();

        var total = publishers.Count;
        if (total == 0) return [];

        var entries = new List<LeaderboardEntry>();
        var currentRank = 1;

        for (var i = 0; i < publishers.Count; i++)
        {
            if (i > 0 && publishers[i].Rating < publishers[i - 1].Rating)
                currentRank = i + 1;

            var percentile = ((double)(total - currentRank + 1) / total) * 100;

            entries.Add(new LeaderboardEntry
            {
                PublisherId = publishers[i].Id,
                Name = publishers[i].Name,
                Nick = publishers[i].Nick,
                Picture = publishers[i].Picture,
                Rating = publishers[i].Rating,
                Rank = currentRank,
                Percentile = Math.Round(percentile, 1),
                Grade = GetGrade(percentile, currentRank)
            });
        }

        return entries;
    }

    private static string GetGrade(double percentile, int rank)
    {
        if (rank == 1) return "S++";
        if (percentile >= 99) return "S+";
        if (percentile >= 95) return "S";
        if (percentile >= 90) return "A++";
        if (percentile >= 80) return "A+";
        if (percentile >= 70) return "A";
        if (percentile >= 60) return "A-";
        if (percentile >= 50) return "B+";
        if (percentile >= 40) return "B";
        if (percentile >= 20) return "C";
        return "D";
    }
}
