using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherService(AppDatabase db, FileService fs, ICacheService cache)
{
    public async Task<Publisher> CreateIndividualPublisher(
        Account.Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFile? picture,
        CloudFile? background
    )
    {
        var publisher = new Publisher
        {
            Type = PublisherType.Individual,
            Name = name ?? account.Name,
            Nick = nick ?? account.Nick,
            Bio = bio ?? account.Profile.Bio,
            Picture = picture ?? account.Profile.Picture,
            Background = background ?? account.Profile.Background,
            AccountId = account.Id,
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = account.Id,
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null) await fs.MarkUsageAsync(publisher.Picture, 1);
        if (publisher.Background is not null) await fs.MarkUsageAsync(publisher.Background, 1);

        return publisher;
    }

    public async Task<Publisher> CreateOrganizationPublisher(
        Realm.Realm realm,
        Account.Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFile? picture,
        CloudFile? background
    )
    {
        var publisher = new Publisher
        {
            Type = PublisherType.Organizational,
            Name = name ?? realm.Slug,
            Nick = nick ?? realm.Name,
            Bio = bio ?? realm.Description,
            Picture = picture ?? realm.Picture,
            Background = background ?? realm.Background,
            RealmId = realm.Id,
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = account.Id,
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null) await fs.MarkUsageAsync(publisher.Picture, 1);
        if (publisher.Background is not null) await fs.MarkUsageAsync(publisher.Background, 1);

        return publisher;
    }

    public class PublisherStats
    {
        public int PostsCreated { get; set; }
        public int StickerPacksCreated { get; set; }
        public int StickersCreated { get; set; }
        public int UpvoteReceived { get; set; }
        public int DownvoteReceived { get; set; }
        public int SubscribersCount { get; set; }
    }

    private const string PublisherStatsCacheKey = "PublisherStats_{0}";
    private const string PublisherFeatureCacheKey = "PublisherFeature_{0}_{1}";

    public async Task<PublisherStats?> GetPublisherStats(string name)
    {
        var cacheKey = string.Format(PublisherStatsCacheKey, name);
        var stats = await cache.GetAsync<PublisherStats>(cacheKey);
        if (stats is not null)
            return stats;

        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == name);
        if (publisher is null) return null;

        var postsCount = await db.Posts.Where(e => e.Publisher.Id == publisher.Id).CountAsync();
        var postsUpvotes = await db.PostReactions
            .Where(r => r.Post.Publisher.Id == publisher.Id && r.Attitude == PostReactionAttitude.Positive)
            .CountAsync();
        var postsDownvotes = await db.PostReactions
            .Where(r => r.Post.Publisher.Id == publisher.Id && r.Attitude == PostReactionAttitude.Negative)
            .CountAsync();

        var stickerPacksId = await db.StickerPacks.Where(e => e.Publisher.Id == publisher.Id).Select(e => e.Id)
            .ToListAsync();
        var stickerPacksCount = stickerPacksId.Count;

        var stickersCount = await db.Stickers.Where(e => stickerPacksId.Contains(e.PackId)).CountAsync();

        var subscribersCount = await db.PublisherSubscriptions.Where(e => e.PublisherId == publisher.Id).CountAsync();

        stats = new PublisherStats
        {
            PostsCreated = postsCount,
            StickerPacksCreated = stickerPacksCount,
            StickersCreated = stickersCount,
            UpvoteReceived = postsUpvotes,
            DownvoteReceived = postsDownvotes,
            SubscribersCount = subscribersCount,
        };

        await cache.SetAsync(cacheKey, stats, TimeSpan.FromMinutes(5));
        return stats;
    }

    public async Task SetFeatureFlag(Guid publisherId, string flag)
    {
        var featureFlag = await db.PublisherFeatures
            .FirstOrDefaultAsync(f => f.PublisherId == publisherId && f.Flag == flag);

        if (featureFlag == null)
        {
            featureFlag = new PublisherFeature
            {
                PublisherId = publisherId,
                Flag = flag,
            };
            db.PublisherFeatures.Add(featureFlag);
        }
        else
        {
            featureFlag.ExpiredAt = SystemClock.Instance.GetCurrentInstant();
        }

        await db.SaveChangesAsync();
        await cache.RemoveAsync(string.Format(PublisherFeatureCacheKey, publisherId, flag));
    }

    public async Task<bool> HasFeature(Guid publisherId, string flag)
    {
        var cacheKey = string.Format(PublisherFeatureCacheKey, publisherId, flag);

        var isEnabled = await cache.GetAsync<bool?>(cacheKey);
        if (isEnabled.HasValue)
            return isEnabled.Value;

        var now = SystemClock.Instance.GetCurrentInstant();
        var featureFlag = await db.PublisherFeatures
            .FirstOrDefaultAsync(f =>
                f.PublisherId == publisherId && f.Flag == flag &&
                (f.ExpiredAt == null || f.ExpiredAt > now)
            );
        if (featureFlag is not null) isEnabled = true;

        await cache.SetAsync(cacheKey, isEnabled!.Value, TimeSpan.FromMinutes(5));
        return isEnabled.Value;
    }

    public async Task<bool> IsMemberWithRole(Guid publisherId, Guid accountId, PublisherMemberRole requiredRole)
    {
        var member = await db.Publishers
            .Where(p => p.Id == publisherId)
            .SelectMany(p => p.Members)
            .FirstOrDefaultAsync(m => m.AccountId == accountId);

        return member != null && member.Role >= requiredRole;
    }
}