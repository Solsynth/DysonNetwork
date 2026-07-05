using System.Globalization;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class SponsorService(
    AppDatabase db,
    RemotePaymentService payments,
    ILocalizationService localization,
    ICacheService cache,
    ILogger<SponsorService> logger
)
{
    public const decimal MinimumBidAmount = 5m;
    public const string ProductIdentifier = "ads.sponsor";
    public static readonly Duration BidDuration = Duration.FromHours(24);

    public async Task<(Guid OrderId, decimal Amount)> CreateSponsorBidAsync(
        SnPost post,
        DyAccount currentUser,
        decimal amount
    )
    {
        if (amount < MinimumBidAmount)
            throw new InvalidOperationException(
                $"Minimum sponsorship bid is {MinimumBidAmount} golds."
            );

        var accountId = Guid.Parse(currentUser.Id);

        var order = await payments.CreateOrder(
            currency: WalletCurrency.GoldenPoint,
            amount: amount.ToString(CultureInfo.InvariantCulture),
            productIdentifier: ProductIdentifier,
            remarks: localization.Get("posts.sponsor.remarks",
                locale: currentUser.Language,
                args: new { title = post.Title ?? post.Id.ToString() }),
            meta: InfraObjectCoder.ConvertObjectToByteString(
                new Dictionary<string, object?>
                {
                    ["account_id"] = accountId,
                    ["post_id"] = post.Id,
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                }
            ).ToByteArray()
        );

        return (Guid.Parse(order.Id), amount);
    }

    public async Task ConfirmSponsorBidAsync(Guid postId, Guid accountId, decimal amount)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var bid = new SnPostSponsorBid
        {
            PostId = postId,
            AccountId = accountId,
            Amount = amount,
            ExpiresAt = now + BidDuration,
        };

        db.PostSponsorBids.Add(bid);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Sponsor bid confirmed: post={PostId}, account={AccountId}, amount={Amount} golds",
            postId, accountId, amount
        );
    }

    public async Task<SnPostSponsorPlacement?> GetCurrentPlacementAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return await db.PostSponsorPlacements
            .Where(p => p.ValidFrom <= now && p.ValidUntil > now)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync();
    }

    public async Task<SnPost?> GetCurrentSponsoredPostAsync()
    {
        var placement = await GetCurrentPlacementAsync();
        if (placement is null) return null;

        var post = await db.Posts
            .Where(p => p.Id == placement.PostId)
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None)
            .Where(p => p.PublisherId == null || !db.Publishers.Any(pub =>
                pub.Id == p.PublisherId && pub.ShadowbanReason != null && pub.ShadowbanReason != PublisherShadowbanReason.None))
            .Include(p => p.Publisher)
            .Include(p => p.ForwardedPost)
            .Include(p => p.RepliedPost)
            .Include(p => p.Categories)
            .Include(p => p.FeaturedRecords)
            .FirstOrDefaultAsync();

        if (post is null) return null;
        post.Sponsored = true;
        return post;
    }

    public async Task<decimal> GetPostTotalSponsorshipAsync(Guid postId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return await db.PostSponsorBids
            .Where(b => b.PostId == postId && b.ExpiresAt > now)
            .SumAsync(b => b.Amount);
    }

    public async Task<List<SnPostSponsorBid>> GetBidHistoryAsync(Guid postId, Guid? requesterAccountId, Guid? postAuthorAccountId)
    {
        if (requesterAccountId is null) return [];
        var isAuthorized = requesterAccountId == postAuthorAccountId;
        var query = db.PostSponsorBids.Where(b => b.PostId == postId);
        if (!isAuthorized)
            query = query.Where(b => b.AccountId == requesterAccountId);
        return await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
    }

    public async Task<List<SponsorLeaderboardEntry>> GetLeaderboardAsync(int take = 20)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return await db.PostSponsorBids
            .Where(b => b.ExpiresAt > now)
            .GroupBy(b => b.PostId)
            .Select(g => new SponsorLeaderboardEntry
            {
                PostId = g.Key,
                TotalAmount = g.Sum(b => b.Amount),
                BidCount = g.Count(),
            })
            .OrderByDescending(e => e.TotalAmount)
            .Take(take)
            .ToListAsync();
    }

    public async Task RunHourlyAuctionAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var hourStart = now.InUtc()
            .Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant()
            .Plus(Duration.FromHours(now.InUtc().Hour));

        var hourEnd = hourStart.Plus(Duration.FromHours(1));

        var existing = await db.PostSponsorPlacements
            .AnyAsync(p => p.ValidFrom == hourStart);
        if (existing) return;

        var candidates = await db.PostSponsorBids
            .Where(b => b.ExpiresAt > now)
            .GroupBy(b => b.PostId)
            .Select(g => new
            {
                PostId = g.Key,
                TotalAmount = g.Sum(b => b.Amount),
            })
            .ToListAsync();

        if (candidates.Count == 0) return;

        var validPostIds = (await db.Posts
            .Where(p => candidates.Select(c => c.PostId).Contains(p.Id))
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None)
            .Where(p => p.PublisherId == null || !db.Publishers.Any(pub =>
                pub.Id == p.PublisherId && pub.ShadowbanReason != null && pub.ShadowbanReason != PublisherShadowbanReason.None))
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        var validCandidates = candidates
            .Where(c => validPostIds.Contains(c.PostId))
            .ToList();
        if (validCandidates.Count == 0) return;

        var totalWeight = validCandidates.Sum(c => (double)c.TotalAmount);
        if (totalWeight <= 0) return;

        var roll = Random.Shared.NextDouble() * totalWeight;
        var cumulative = 0d;
        var winner = validCandidates[0];
        foreach (var candidate in validCandidates)
        {
            cumulative += (double)candidate.TotalAmount;
            if (roll < cumulative)
            {
                winner = candidate;
                break;
            }
        }

        db.PostSponsorPlacements.Add(new SnPostSponsorPlacement
        {
            PostId = winner.PostId,
            TotalAmount = winner.TotalAmount,
            ValidFrom = hourStart,
            ValidUntil = hourEnd,
        });
        await db.SaveChangesAsync();

        await UpsertAggregatedStatsAsync(winner.PostId);

        logger.LogInformation(
            "Sponsor auction: post {PostId} won hour {HourStart} with {Amount} golds (weighted)",
            winner.PostId, hourStart, winner.TotalAmount
        );
    }

    private async Task UpsertAggregatedStatsAsync(Guid postId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var activeBids = await db.PostSponsorBids
            .Where(b => b.PostId == postId && b.ExpiresAt > now)
            .ToListAsync();

        var isCurrentlyPlaced = await db.PostSponsorPlacements
            .AnyAsync(p => p.PostId == postId && p.ValidFrom <= now && p.ValidUntil > now);

        var stats = await db.PostAggregatedStats.FirstOrDefaultAsync(s => s.PostId == postId);
        if (stats is null)
        {
            stats = new SnPostAggregatedStats { PostId = postId };
            db.PostAggregatedStats.Add(stats);
        }

        stats.ActiveBidTotal = activeBids.Sum(b => (decimal?)b.Amount) ?? 0m;
        stats.ActiveBidCount = activeBids.Count;
        stats.IsCurrentlyPlaced = isCurrentlyPlaced;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            stats = await db.PostAggregatedStats.FirstAsync(s => s.PostId == postId);
            stats.ActiveBidTotal = activeBids.Sum(b => (decimal?)b.Amount) ?? 0m;
            stats.ActiveBidCount = activeBids.Count;
            stats.IsCurrentlyPlaced = isCurrentlyPlaced;
            await db.SaveChangesAsync();
        }
    }

    private static int GetAdIntervalPerk(int perkLevel) => perkLevel switch
    {
        >= 3 => 0,
        2 => 20,
        1 => 10,
        _ => 5,
    };

    public async Task<bool> IsAdSlotEligibleAsync(string viewerKey, int perkLevel)
    {
        var interval = GetAdIntervalPerk(perkLevel);
        if (interval <= 0) return false;

        var cacheKey = $"timeline:ad-counter:{viewerKey}";
        var lockKey = $"timeline:ad-counter-lock:{viewerKey}";
        var result = await cache.ExecuteWithLockAsync(lockKey, async () =>
        {
            var count = await cache.GetAsync<long?>(cacheKey) ?? 0;
            count++;
            await cache.SetAsync(cacheKey, count, TimeSpan.FromHours(24));
            return count % interval == 0;
        }, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));

        return result.Acquired && result.Result == true;
    }

    public async Task<SnPost?> TryGetTimelineSponsoredPostAsync(string viewerKey, int perkLevel)
    {
        if (!await IsAdSlotEligibleAsync(viewerKey, perkLevel))
            return null;

        var post = await GetCurrentSponsoredPostAsync();
        if (post is null)
            return null;

        await RecordImpressionAsync(post.Id);
        return post;
    }

    public async Task RecordImpressionAsync(Guid postId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var stats = await db.PostAggregatedStats.FirstOrDefaultAsync(s => s.PostId == postId);
        if (stats is null)
        {
            stats = new SnPostAggregatedStats
            {
                PostId = postId,
                ShownCount = 1,
                LastShownAt = now,
            };
            db.PostAggregatedStats.Add(stats);
        }
        else
        {
            stats.ShownCount++;
            stats.LastShownAt = now;
            db.PostAggregatedStats.Update(stats);
        }

        await db.SaveChangesAsync();

        logger.LogDebug(
            "Ad impression recorded for post {PostId} at {Now}, total shown: {Count}",
            postId, now, stats.ShownCount
        );
    }

    public async Task<List<AdvertisingPostStats>> ListAdvertisingPostsAsync(Guid publisherId)
    {
        var stats = await db.PostAggregatedStats
            .Where(s => s.Post.PublisherId == publisherId)
            .Include(s => s.Post)
            .Where(s => s.ActiveBidTotal > 0 || s.ShownCount > 0)
            .OrderByDescending(s => s.ActiveBidTotal)
            .ThenByDescending(s => s.ShownCount)
            .ToListAsync();

        return stats.Select(s => new AdvertisingPostStats
        {
            PostId = s.PostId,
            Title = s.Post.Title,
            Slug = s.Post.Slug,
            ActiveBidTotal = s.ActiveBidTotal,
            BidCount = s.ActiveBidCount,
            IsCurrentlyPlaced = s.IsCurrentlyPlaced,
            ShownCount = s.ShownCount,
            LastShownAt = s.LastShownAt,
        }).ToList();
    }

    public async Task<List<PublicAdvertisingPostStats>> ListPublicAdvertisingPostsAsync(Guid publisherId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var activeBids = await db.PostSponsorBids
            .Where(b => b.ExpiresAt > now)
            .GroupBy(b => b.PostId)
            .Select(g => new
            {
                PostId = g.Key,
                TotalAmount = g.Sum(b => b.Amount),
                BidCount = g.Count(),
            })
            .ToListAsync();

        var validCandidateIds = (await db.Posts
            .Where(p => activeBids.Select(b => b.PostId).Contains(p.Id))
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None)
            .Where(p => p.PublisherId == null || !db.Publishers.Any(pub =>
                pub.Id == p.PublisherId && pub.ShadowbanReason != null && pub.ShadowbanReason != PublisherShadowbanReason.None))
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        var totalDisplayWeight = activeBids
            .Where(b => validCandidateIds.Contains(b.PostId))
            .Sum(b => b.TotalAmount);
        var activeBidByPostId = activeBids.ToDictionary(b => b.PostId);

        var posts = await db.Posts
            .Where(p => p.PublisherId == publisherId)
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
            })
            .ToListAsync();
        var postIds = posts.Select(p => p.Id).ToList();

        var aggregatedStats = await db.PostAggregatedStats
            .Where(s => postIds.Contains(s.PostId))
            .Select(s => new
            {
                s.PostId,
                s.ShownCount,
                s.LastShownAt,
            })
            .ToListAsync();
        var aggregatedStatsByPostId = aggregatedStats.ToDictionary(s => s.PostId);

        var currentPlacementIds = (await db.PostSponsorPlacements
            .Where(p => postIds.Contains(p.PostId))
            .Where(p => p.ValidFrom <= now && p.ValidUntil > now)
            .Select(p => p.PostId)
            .ToListAsync()).ToHashSet();

        return posts
            .Select(post =>
            {
                activeBidByPostId.TryGetValue(post.Id, out var bid);
                aggregatedStatsByPostId.TryGetValue(post.Id, out var stats);

                var activeBidTotal = bid?.TotalAmount ?? 0m;
                return new PublicAdvertisingPostStats
                {
                    PostId = post.Id,
                    Title = post.Title,
                    Slug = post.Slug,
                    ActiveBidTotal = activeBidTotal,
                    BidCount = bid?.BidCount ?? 0,
                    IsCurrentlyPlaced = currentPlacementIds.Contains(post.Id),
                    ShownCount = stats?.ShownCount ?? 0,
                    LastShownAt = stats?.LastShownAt,
                    DisplayChance = totalDisplayWeight > 0m && validCandidateIds.Contains(post.Id)
                        ? activeBidTotal / totalDisplayWeight
                        : 0m,
                };
            })
            .Where(s => s.ActiveBidTotal > 0 || s.ShownCount > 0 || s.IsCurrentlyPlaced)
            .OrderByDescending(s => s.ActiveBidTotal)
            .ThenByDescending(s => s.ShownCount)
            .ToList();
    }
}

public class SponsorLeaderboardEntry
{
    public Guid PostId { get; set; }
    public decimal TotalAmount { get; set; }
    public int BidCount { get; set; }
}

public class AdvertisingPostStats
{
    public Guid PostId { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public decimal ActiveBidTotal { get; set; }
    public int BidCount { get; set; }
    public bool IsCurrentlyPlaced { get; set; }
    public long ShownCount { get; set; }
    public Instant? LastShownAt { get; set; }
}

public class PublicAdvertisingPostStats : AdvertisingPostStats
{
    public decimal DisplayChance { get; set; }
}
