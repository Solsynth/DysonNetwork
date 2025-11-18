using System.Globalization;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherType = DysonNetwork.Shared.Models.PublisherType;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherService(
    AppDatabase db,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    SocialCreditService.SocialCreditServiceClient socialCredits,
    ExperienceService.ExperienceServiceClient experiences,
    ICacheService cache,
    RemoteAccountService remoteAccounts
)
{
    public async Task<SnPublisher?> GetPublisherByName(string name)
    {
        return await db.Publishers
            .Where(e => e.Name == name)
            .FirstOrDefaultAsync();
    }

    private const string UserPublishersCacheKey = "accounts:{0}:publishers";

    public async Task<List<SnPublisher>> GetUserPublishers(Guid userId)
    {
        var cacheKey = string.Format(UserPublishersCacheKey, userId);

        // Try to get publishers from the cache first
        var publishers = await cache.GetAsync<List<SnPublisher>>(cacheKey);
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

    public async Task<Dictionary<Guid, List<SnPublisher>>> GetUserPublishersBatch(List<Guid> userIds)
    {
        var result = new Dictionary<Guid, List<SnPublisher>>();
        var missingIds = new List<Guid>();

        // Try to get publishers from cache for each user
        foreach (var userId in userIds)
        {
            var cacheKey = string.Format(UserPublishersCacheKey, userId);
            var publishers = await cache.GetAsync<List<SnPublisher>>(cacheKey);
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

    public async Task<List<SnPublisher>> GetSubscribedPublishers(Guid userId)
    {
        var cacheKey = string.Format(SubscribedPublishersCacheKey, userId);

        // Try to get publishers from the cache first
        var publishers = await cache.GetAsync<List<SnPublisher>>(cacheKey);
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

    public async Task<List<SnPublisherMember>> GetPublisherMembers(Guid publisherId)
    {
        var cacheKey = string.Format(PublisherMembersCacheKey, publisherId);

        // Try to get members from the cache first
        var members = await cache.GetAsync<List<SnPublisherMember>>(cacheKey);
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

    public async Task<SnPublisher> CreateIndividualPublisher(
        Account account,
        string? name,
        string? nick,
        string? bio,
        SnCloudFileReferenceObject? picture,
        SnCloudFileReferenceObject? background
    )
    {
        var publisher = new SnPublisher
        {
            Type = PublisherType.Individual,
            Name = name ?? account.Name,
            Nick = nick ?? account.Nick,
            Bio = bio ?? account.Profile.Bio,
            Picture = picture ?? (account.Profile.Picture is null
                ? null
                : SnCloudFileReferenceObject.FromProtoValue(account.Profile.Picture)),
            Background = background ?? (account.Profile.Background is null
                ? null
                : SnCloudFileReferenceObject.FromProtoValue(account.Profile.Background)),
            AccountId = Guid.Parse(account.Id),
            Members =
            [
                new()
                {
                    AccountId = Guid.Parse(account.Id),
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            ]
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

    public async Task<SnPublisher> CreateOrganizationPublisher(
        SnRealm realm,
        Account account,
        string? name,
        string? nick,
        string? bio,
        SnCloudFileReferenceObject? picture,
        SnCloudFileReferenceObject? background
    )
    {
        var publisher = new SnPublisher
        {
            Type = PublisherType.Organizational,
            Name = name ?? realm.Slug,
            Nick = nick ?? realm.Name,
            Bio = bio ?? realm.Description,
            Picture = picture ?? SnCloudFileReferenceObject.FromProtoValue(account.Profile.Picture),
            Background = background ?? SnCloudFileReferenceObject.FromProtoValue(account.Profile.Background),
            RealmId = realm.Id,
            Members = new List<SnPublisherMember>
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

    private const string PublisherStatsCacheKey = "publisher:{0}:stats";
    private const string PublisherHeatmapCacheKey = "publisher:{0}:heatmap";
    private const string PublisherFeatureCacheKey = "publisher:{0}:feature:{1}";

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
            .Where(r => r.Post.Publisher.Id == publisher.Id &&
                        r.Attitude == Shared.Models.PostReactionAttitude.Positive)
            .CountAsync();
        var postsDownvotes = await db.PostReactions
            .Where(r => r.Post.Publisher.Id == publisher.Id &&
                        r.Attitude == Shared.Models.PostReactionAttitude.Negative)
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

    public async Task<ActivityHeatmap?> GetPublisherHeatmap(string name)
    {
        var cacheKey = string.Format(PublisherHeatmapCacheKey, name);
        var heatmap = await cache.GetAsync<ActivityHeatmap?>(cacheKey);
        if (heatmap is not null)
            return heatmap;

        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name == name);
        if (publisher is null) return null;

        var now = SystemClock.Instance.GetCurrentInstant();
        var periodStart = now.Minus(Duration.FromDays(365));
        var periodEnd = now;

        var postGroups = await db.Posts
            .Where(p => p.PublisherId == publisher.Id && p.CreatedAt >= periodStart && p.CreatedAt <= periodEnd)
            .Select(p => p.CreatedAt.InUtc().Date)
            .GroupBy(d => d)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();

        var items = postGroups.Select(p => new ActivityHeatmapItem
        {
            Date = p.Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant(),
            Count = p.Count
        }).ToList();

        heatmap = new ActivityHeatmap
        {
            Unit = "posts",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Items = items.OrderBy(i => i.Date).ToList()
        };

        await cache.SetAsync(cacheKey, heatmap, TimeSpan.FromMinutes(5));
        return heatmap;
    }

    public async Task SetFeatureFlag(Guid publisherId, string flag)
    {
        var featureFlag = await db.PublisherFeatures
            .FirstOrDefaultAsync(f => f.PublisherId == publisherId && f.Flag == flag);

        if (featureFlag == null)
        {
            featureFlag = new SnPublisherFeature
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

    public async Task<bool> IsMemberWithRole(Guid publisherId, Guid accountId,
        PublisherMemberRole requiredRole)
    {
        var member = await db.Publishers
            .Where(p => p.Id == publisherId)
            .SelectMany(p => p.Members)
            .FirstOrDefaultAsync(m => m.AccountId == accountId);

        return member != null && member.Role >= requiredRole;
    }

    public async Task<SnPublisherMember> LoadMemberAccount(SnPublisherMember member)
    {
        var account = await remoteAccounts.GetAccount(member.AccountId);
        member.Account = SnAccount.FromProtoValue(account);
        return member;
    }

    public async Task<List<SnPublisherMember>> LoadMemberAccounts(ICollection<SnPublisherMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accounts = (await remoteAccounts.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        return
        [
            .. members.Select(m =>
            {
                if (accounts.TryGetValue(m.AccountId, out var account))
                    m.Account = SnAccount.FromProtoValue(account);
                return m;
            })
        ];
    }

    public async Task<List<SnPublisher>> LoadIndividualPublisherAccounts(ICollection<SnPublisher> publishers)
    {
        var accountIds = publishers
            .Where(p => p.AccountId.HasValue && p.Type == PublisherType.Individual)
            .Select(p => p.AccountId!.Value)
            .ToList();
        if (accountIds.Count == 0) return publishers.ToList();

        var accounts = (await remoteAccounts.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        foreach (var p in publishers)
        {
            if (p.AccountId.HasValue && accounts.TryGetValue(p.AccountId.Value, out var account))
                p.Account = SnAccount.FromProtoValue(account);
        }

        return publishers.ToList();
    }

    public class PublisherRewardPreview
    {
        public int Experience { get; set; }
        public int SocialCredits { get; set; }
    }

    public async Task<PublisherRewardPreview> GetPublisherExpectedReward(Guid publisherId)
    {
        var cacheKey = $"publisher:{publisherId}:rewards";
        var (found, cached) = await cache.GetAsyncWithStatus<PublisherRewardPreview>(cacheKey);
        if (found)
            return cached!;

        var now = SystemClock.Instance.GetCurrentInstant();
        var yesterday = now.InZone(DateTimeZone.Utc).Date.PlusDays(-1);
        var periodStart = yesterday.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var periodEnd = periodStart.Plus(Duration.FromDays(1)).Minus(Duration.FromMilliseconds(1));

        // Get posts stats for this publisher: count, id, exclude content
        var postsInPeriod = await db.Posts
            .Where(p => p.PublisherId == publisherId && p.CreatedAt >= periodStart && p.CreatedAt <= periodEnd)
            .Select(p => new { Id = p.Id, AwardedScore = p.AwardedScore })
            .ToListAsync();

        // Get reactions for these posts
        var postIds = postsInPeriod.Select(p => p.Id).ToList();
        var reactions = await db.PostReactions
            .Where(r => postIds.Contains(r.PostId))
            .ToListAsync();

        if (postsInPeriod.Count == 0)
            return new PublisherRewardPreview { Experience = 0, SocialCredits = 0 };

        // Calculate stats
        var postCount = postsInPeriod.Count;
        var upvotes = reactions.Count(r => r.Attitude == Shared.Models.PostReactionAttitude.Positive);
        var downvotes = reactions.Count(r => r.Attitude == Shared.Models.PostReactionAttitude.Negative);
        var awardScore = postsInPeriod.Sum(p => (double)p.AwardedScore);

        // Each post counts as 100 experiences,
        // and each point (upvote - downvote + award score * 0.1) count as 10 experiences
        var netVotes = upvotes - downvotes;
        var points = netVotes + awardScore * 0.1;
        var experienceFromPosts = postCount * 100;
        var experienceFromPoints = (int)(points * 10);
        var totalExperience = experienceFromPosts + experienceFromPoints;

        var preview = new PublisherRewardPreview
        {
            Experience = totalExperience,
            SocialCredits = (int)(points * 10)
        };

        await cache.SetAsync(cacheKey, preview, TimeSpan.FromMinutes(5));
        return preview;
    }

    public async Task SettlePublisherRewards()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var yesterday = now.InZone(DateTimeZone.Utc).Date.PlusDays(-1);
        var periodStart = yesterday.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var periodEnd = periodStart.Plus(Duration.FromDays(1)).Minus(Duration.FromMilliseconds(1));

        // Get posts stats: count, publisher id, exclude content
        var postsInPeriod = await db.Posts
            .Where(p => p.CreatedAt >= periodStart && p.CreatedAt <= periodEnd)
            .Select(p => new { Id = p.Id, PublisherId = p.PublisherId, AwardedScore = p.AwardedScore })
            .ToListAsync();

        // Get reactions for these posts
        var postIds = postsInPeriod.Select(p => p.Id).ToList();
        var reactions = await db.PostReactions
            .Where(r => postIds.Contains(r.PostId))
            .ToListAsync();

        // Group stats by publisher id
        var postIdToPublisher = postsInPeriod.ToDictionary(p => p.Id, p => p.PublisherId);
        var publisherStats = postsInPeriod
            .GroupBy(p => p.PublisherId)
            .ToDictionary(g => g.Key,
                g => new
                {
                    PostCount = g.Count(), Upvotes = 0, Downvotes = 0, AwardScore = g.Sum(p => (double)p.AwardedScore)
                });

        foreach (var reaction in reactions.Where(r => r.Attitude == Shared.Models.PostReactionAttitude.Positive))
        {
            if (!postIdToPublisher.TryGetValue(reaction.PostId, out var pubId) ||
                !publisherStats.TryGetValue(pubId, out var stat)) continue;
            stat = new { stat.PostCount, Upvotes = stat.Upvotes + 1, stat.Downvotes, stat.AwardScore };
            publisherStats[pubId] = stat;
        }

        foreach (var reaction in reactions.Where(r => r.Attitude == Shared.Models.PostReactionAttitude.Negative))
        {
            if (!postIdToPublisher.TryGetValue(reaction.PostId, out var pubId) ||
                !publisherStats.TryGetValue(pubId, out var stat)) continue;
            stat = new { stat.PostCount, stat.Upvotes, Downvotes = stat.Downvotes + 1, stat.AwardScore };
            publisherStats[pubId] = stat;
        }

        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var publisherIds = publisherStats.Keys.ToList();
        var publishers = await db.Publishers
            .Where(p => publisherIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p);
        var publisherMembers = await db.PublisherMembers
            .Where(m => publisherIds.Contains(m.PublisherId))
            .ToListAsync();
        var accountIds = publisherMembers.Select(m => m.AccountId).ToList();
        var accounts = (await remoteAccounts.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);
        var publisherAccounts = publisherMembers
            .GroupBy(m => m.PublisherId)
            .ToDictionary(g => g.Key, g => g.Select(m => SnAccount.FromProtoValue(accounts[m.AccountId])).ToList());

        // Foreach loop through publishers to calculate experience
        foreach (var (publisherId, value) in publisherStats)
        {
            var postCount = value.PostCount;
            var upvotes = value.Upvotes;
            var downvotes = value.Downvotes;
            var awardScore = value.AwardScore; // Fetch or calculate here

            // Each post counts as 100 experiences,
            // and each point (upvote - downvote + award score * 0.1) count as 10 experiences
            var netVotes = upvotes - downvotes;
            var points = netVotes + awardScore * 0.1;
            var experienceFromPosts = postCount * 100;
            var experienceFromPoints = (int)(points * 10);
            var totalExperience = experienceFromPosts + experienceFromPoints;

            if (!publisherAccounts.TryGetValue(publisherId, out var receivers) || receivers.Count == 0)
                continue;

            var publisherName = publishers.TryGetValue(publisherId, out var pub) ? pub.Name : "unknown";

            // Use totalExperience for rewarding
            foreach (var receiver in receivers)
            {
                await experiences.AddRecordAsync(new AddExperienceRecordRequest
                {
                    Reason = $"Publishing Reward on {date} for @{publisherName}",
                    ReasonType = "publishers.rewards",
                    AccountId = receiver.Id.ToString(),
                    Delta = totalExperience,
                });
            }
        }

        // Foreach loop through publishers to set social credit
        var expiredAt = now.InZone(DateTimeZone.Utc).Date.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc)
            .Minus(Duration.FromMilliseconds(1)).ToInstant();
        foreach (var (publisherId, value) in publisherStats)
        {
            var upvotes = value.Upvotes;
            var downvotes = value.Downvotes;
            var awardScore = value.AwardScore; // Fetch or calculate here

            var netVotes = upvotes - downvotes;
            var points = netVotes + awardScore * 0.1;
            var socialCreditDelta = (int)(points * 10);

            if (socialCreditDelta == 0) continue;

            if (!publisherAccounts.TryGetValue(publisherId, out var receivers) || receivers.Count == 0)
                continue;
            
            var publisherName = publishers.TryGetValue(publisherId, out var pub) ? pub.Name : "unknown";

            // Set social credit for receivers, expired before next settle
            foreach (var receiver in receivers)
            {
                await socialCredits.AddRecordAsync(new AddSocialCreditRecordRequest
                {
                    Reason = $"Publishing Reward on {date} for @{publisherName}",
                    ReasonType = "publishers.rewards",
                    AccountId = receiver.Id.ToString(),
                    Delta = socialCreditDelta,
                    ExpiredAt = expiredAt.ToTimestamp(),
                });
            }
        }
    }
}