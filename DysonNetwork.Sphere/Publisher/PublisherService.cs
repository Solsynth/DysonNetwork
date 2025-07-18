using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherService(
    AppDatabase db,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    ICacheService cache
)
{
    public async Task<Publisher?> GetPublisherByName(string name)
    {
        return await db.Publishers
            .Where(e => e.Name == name)
            .FirstOrDefaultAsync();
    }

    private const string UserPublishersCacheKey = "accounts:{0}:publishers";

    public async Task<List<Publisher>> GetUserPublishers(Guid userId)
    {
        var cacheKey = string.Format(UserPublishersCacheKey, userId);

        // Try to get publishers from the cache first
        var publishers = await cache.GetAsync<List<Publisher>>(cacheKey);
        if (publishers is not null)
            return publishers;

        // If not in cache, fetch from a database
        var publishersId = await db.PublisherMembers
            .Where(p => p.AccountId == userId)
            .Select(p => p.PublisherId)
            .ToListAsync();
        publishers = await db.Publishers
            .Where(p => publishersId.Contains(p.Id))
            .ToListAsync();

        // Store in a cache for 5 minutes
        await cache.SetAsync(cacheKey, publishers, TimeSpan.FromMinutes(5));

        return publishers;
    }

    public async Task<Dictionary<Guid, List<Publisher>>> GetUserPublishersBatch(List<Guid> userIds)
    {
        var result = new Dictionary<Guid, List<Publisher>>();
        var missingIds = new List<Guid>();

        // Try to get publishers from cache for each user
        foreach (var userId in userIds)
        {
            var cacheKey = string.Format(UserPublishersCacheKey, userId);
            var publishers = await cache.GetAsync<List<Publisher>>(cacheKey);
            if (publishers != null)
                result[userId] = publishers;
            else
                missingIds.Add(userId);
        }

        if (missingIds.Count <= 0) return result;
        {
            // Fetch missing data from database
            var publisherMembers = await db.PublisherMembers
                .Where(p => missingIds.Contains(p.AccountId))
                .Select(p => new { p.AccountId, p.PublisherId })
                .ToListAsync();

            var publisherIds = publisherMembers.Select(p => p.PublisherId).Distinct().ToList();
            var publishers = await db.Publishers
                .Where(p => publisherIds.Contains(p.Id))
                .ToListAsync();

            // Group publishers by user id
            foreach (var userId in missingIds)
            {
                var userPublisherIds = publisherMembers
                    .Where(p => p.AccountId == userId)
                    .Select(p => p.PublisherId)
                    .ToList();

                var userPublishers = publishers
                    .Where(p => userPublisherIds.Contains(p.Id))
                    .ToList();

                result[userId] = userPublishers;

                // Cache individual results
                var cacheKey = string.Format(UserPublishersCacheKey, userId);
                await cache.SetAsync(cacheKey, userPublishers, TimeSpan.FromMinutes(5));
            }
        }

        return result;
    }


    public const string SubscribedPublishersCacheKey = "accounts:{0}:subscribed-publishers";

    public async Task<List<Publisher>> GetSubscribedPublishers(Guid userId)
    {
        var cacheKey = string.Format(SubscribedPublishersCacheKey, userId);

        // Try to get publishers from the cache first
        var publishers = await cache.GetAsync<List<Publisher>>(cacheKey);
        if (publishers is not null)
            return publishers;

        // If not in cache, fetch from a database
        var publishersId = await db.PublisherSubscriptions
            .Where(p => p.AccountId == userId)
            .Where(p => p.Status == PublisherSubscriptionStatus.Active)
            .Select(p => p.PublisherId)
            .ToListAsync();
        publishers = await db.Publishers
            .Where(p => publishersId.Contains(p.Id))
            .ToListAsync();

        // Store in a cache for 5 minutes
        await cache.SetAsync(cacheKey, publishers, TimeSpan.FromMinutes(5));

        return publishers;
    }

    private const string PublisherMembersCacheKey = "publishers:{0}:members";

    public async Task<List<PublisherMember>> GetPublisherMembers(Guid publisherId)
    {
        var cacheKey = string.Format(PublisherMembersCacheKey, publisherId);

        // Try to get members from the cache first
        var members = await cache.GetAsync<List<PublisherMember>>(cacheKey);
        if (members is not null)
            return members;

        // If not in cache, fetch from a database
        members = await db.PublisherMembers
            .Where(p => p.PublisherId == publisherId)
            .ToListAsync();

        // Store in cache for 5 minutes (consistent with other cache durations in the class)
        await cache.SetAsync(cacheKey, members, TimeSpan.FromMinutes(5));

        return members;
    }

    public async Task<Publisher> CreateIndividualPublisher(
        Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFileReferenceObject? picture,
        CloudFileReferenceObject? background
    )
    {
        var publisher = new Publisher
        {
            Type = PublisherType.Individual,
            Name = name ?? account.Name,
            Nick = nick ?? account.Nick,
            Bio = bio ?? account.Profile.Bio,
            Picture = picture ?? (account.Profile.Picture is null
                ? null
                : CloudFileReferenceObject.FromProtoValue(account.Profile.Picture)),
            Background = background ?? (account.Profile.Background is null
                ? null
                : CloudFileReferenceObject.FromProtoValue(account.Profile.Background)),
            AccountId = Guid.Parse(account.Id),
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = Guid.Parse(account.Id),
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null)
        {
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = publisher.Picture.Id,
                    Usage = "publisher.picture",
                    ResourceId = publisher.ResourceIdentifier,
                }
            );
        }

        if (publisher.Background is not null)
        {
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = publisher.Background.Id,
                    Usage = "publisher.background",
                    ResourceId = publisher.ResourceIdentifier,
                }
            );
        }

        return publisher;
    }

    public async Task<Publisher> CreateOrganizationPublisher(
        Realm.Realm realm,
        Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFileReferenceObject? picture,
        CloudFileReferenceObject? background
    )
    {
        var publisher = new Publisher
        {
            Type = PublisherType.Organizational,
            Name = name ?? realm.Slug,
            Nick = nick ?? realm.Name,
            Bio = bio ?? realm.Description,
            Picture = picture ?? CloudFileReferenceObject.FromProtoValue(account.Profile.Picture),
            Background = background ?? CloudFileReferenceObject.FromProtoValue(account.Profile.Background),
            RealmId = realm.Id,
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = Guid.Parse(account.Id),
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null)
        {
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = publisher.Picture.Id,
                    Usage = "publisher.picture",
                    ResourceId = publisher.ResourceIdentifier,
                }
            );
        }

        if (publisher.Background is not null)
        {
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = publisher.Background.Id,
                    Usage = "publisher.background",
                    ResourceId = publisher.ResourceIdentifier,
                }
            );
        }

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
        isEnabled = featureFlag is not null;

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