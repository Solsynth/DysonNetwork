using System.Globalization;
using DysonNetwork.Sphere.Models;
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

        var winner = await db.PostSponsorBids
            .Where(b => b.ExpiresAt > now)
            .GroupBy(b => b.PostId)
            .Select(g => new { PostId = g.Key, TotalAmount = g.Sum(b => b.Amount) })
            .OrderByDescending(g => g.TotalAmount)
            .FirstOrDefaultAsync();

        if (winner is null) return;

        var post = await db.Posts
            .Where(p => p.Id == winner.PostId && p.DeletedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();

        if (post == Guid.Empty) return;

        db.PostSponsorPlacements.Add(new SnPostSponsorPlacement
        {
            PostId = winner.PostId,
            TotalAmount = winner.TotalAmount,
            ValidFrom = hourStart,
            ValidUntil = hourEnd,
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Sponsor auction: post {PostId} won hour {HourStart} with {Amount} golds",
            winner.PostId, hourStart, winner.TotalAmount
        );
    }
}

public class SponsorLeaderboardEntry
{
    public Guid PostId { get; set; }
    public decimal TotalAmount { get; set; }
    public int BidCount { get; set; }
}
