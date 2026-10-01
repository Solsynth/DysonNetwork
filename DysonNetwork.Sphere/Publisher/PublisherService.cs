using System.Globalization;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.ActivityPub.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using Npgsql;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherType = DysonNetwork.Shared.Models.PublisherType;

namespace DysonNetwork.Sphere.Publisher;

public class FediverseStatus
{
    public bool Enabled { get; set; }
    public SnPublisher? Actor { get; set; }
    public int FollowerCount { get; set; }
    public string? ActorUri { get; set; }
}

public class PublisherService(
    AppDatabase db,
    DySocialCreditService.DySocialCreditServiceClient socialCredits,
    DyExperienceService.DyExperienceServiceClient experiences,
    PublisherRatingService ratingService,
    ICacheService cache,
    ILocalizationService localization,
    RemoteAccountService remoteAccounts,
    RemoteRealmService remoteRealms,
    IConfiguration configuration,
    ILogger<PublisherService> logger,
    RemoteMerchantService merchantRpc
)
{
    public async Task<SnPublisher?> GetPublisherLoaded(Guid id)
    {
        var publisher = await db.Publishers
            .Where(p => p.Id == id)
            .FirstOrDefaultAsync();
        return publisher is null ? null : (await LoadIndividualPublisherAccounts([publisher])).First();
    }

    public async Task<SnPublisher?> GetPublisherByName(string name)
    {
        var lowerName = name.ToLowerInvariant();
        return await db.Publishers
            .Where(e => e.Name.ToLower() == lowerName)
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
            .Where(p => !p.EndedAt.HasValue)
            .Where(p => p.State == PublisherSubscriptionState.Accepted && !p.IsBlocking)
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

    public async Task<List<SnPublisherMember>> GetPublisherMembers(Guid publisherId, bool includePending = false)
    {
        var cacheKey = string.Format(PublisherMembersCacheKey, publisherId);

        // Try to get members from the cache first
        var members = await cache.GetAsync<List<SnPublisherMember>>(cacheKey);
        if (members is not null)
            return members;

        // If not in cache, fetch from a database
        members = await db.PublisherMembers
            .Where(p => includePending || p.JoinedAt.HasValue)
            .Where(p => p.PublisherId == publisherId)
            .ToListAsync();

        // Store in cache for 5 minutes (consistent with other cache durations in the class)
        await cache.SetAsync(cacheKey, members, TimeSpan.FromMinutes(5));

        return members;
    }

    public async Task<SnPublisher> CreateIndividualPublisher(
        DyAccount account,
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
            Bio = bio ?? account.Profile?.Bio,
            Picture = picture ?? (account.Profile?.Picture is null
                ? null
                : SnCloudFileReferenceObject.FromProtoValue(account.Profile.Picture)),
            Background = background ?? (account.Profile?.Background is null
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

        return publisher;
    }

    public async Task<SnPublisher> CreateOrganizationPublisher(
        SnRealm realm,
        DyAccount account,
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
            Picture = picture ?? (account.Profile?.Picture is null
                ? null
                : SnCloudFileReferenceObject.FromProtoValue(account.Profile.Picture)),
            Background = background ?? (account.Profile?.Background is null
                ? null
                : SnCloudFileReferenceObject.FromProtoValue(account.Profile.Background)),
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

        var lowerName = name.ToLowerInvariant();
        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name.ToLower() == lowerName);
        if (publisher is null) return null;

        var postsCount = await db.Posts.Where(e => e.PublisherId == publisher.Id).CountAsync();
        var postsUpvotes = await db.PostReactions
            .Where(r => r.Post.PublisherId == publisher.Id &&
                        r.Attitude == Shared.Models.PostReactionAttitude.Positive)
            .CountAsync();
        var postsDownvotes = await db.PostReactions
            .Where(r => r.Post.PublisherId == publisher.Id &&
                        r.Attitude == Shared.Models.PostReactionAttitude.Negative)
            .CountAsync();

        var stickerPacksId = await db.StickerPacks
            .Where(e => e.PublisherId == publisher.Id)
            .Select(e => e.Id)
            .ToListAsync();
        var stickerPacksCount = stickerPacksId.Count;

        var stickersCount = await db.Stickers.Where(e => stickerPacksId.Contains(e.PackId)).CountAsync();

        var subscribersCount = await db.PublisherSubscriptions
            .Where(e => e.PublisherId == publisher.Id
                && e.EndedAt == null
                && e.State == PublisherSubscriptionState.Accepted
                && !e.IsBlocking)
            .CountAsync();

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

        var lowerName = name.ToLowerInvariant();
        var publisher = await db.Publishers.FirstOrDefaultAsync(e => e.Name.ToLower() == lowerName);
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

    public async Task<bool> HasFollowRequiresApprovalFlag(Guid publisherId)
    {
        var cacheKey = string.Format(PublisherFeatureCacheKey, publisherId, PublisherFeatureFlag.FollowRequiresApproval);
        var cached = await cache.GetAsync<bool?>(cacheKey);
        if (cached.HasValue)
            return cached.Value;

        var publisher = await db.Publishers.Where(p => p.Id == publisherId).Select(p => new { p.ModerateSubscription }).FirstOrDefaultAsync();
        var result = publisher?.ModerateSubscription ?? false;
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<bool> HasPostsRequireFollowFlag(Guid publisherId)
    {
        var cacheKey = string.Format(PublisherFeatureCacheKey, publisherId, PublisherFeatureFlag.PostsRequireFollow);
        var cached = await cache.GetAsync<bool?>(cacheKey);
        if (cached.HasValue)
            return cached.Value;

        var publisher = await db.Publishers.Where(p => p.Id == publisherId).Select(p => new { p.GatekeptFollows }).FirstOrDefaultAsync();
        var result = publisher?.GatekeptFollows ?? false;
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<bool> IsFollower(Guid publisherId, Guid accountId)
    {
        return await db.PublisherSubscriptions
            .AnyAsync(r =>
                r.PublisherId == publisherId &&
                r.AccountId == accountId &&
                r.State == PublisherSubscriptionState.Accepted &&
                r.EndedAt == null
            );
    }

    public async Task<bool> HasAcceptedFollowRequest(Guid publisherId, Guid accountId)
    {
        return await db.PublisherSubscriptions
            .AnyAsync(r =>
                r.PublisherId == publisherId &&
                r.AccountId == accountId &&
                r.State == PublisherSubscriptionState.Accepted &&
                r.EndedAt == null
            );
    }

    public async Task<SnPublisherSubscription?> GetFollowRequest(Guid publisherId, Guid accountId)
    {
        return await db.PublisherSubscriptions
            .FirstOrDefaultAsync(r =>
                r.PublisherId == publisherId &&
                r.AccountId == accountId
            );
    }

    public async Task<SnPublisherSubscription> CreateFollowRequest(Guid publisherId, Guid accountId)
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher?.AccountId is not null)
        {
            var isBlocked = await remoteAccounts.IsBlockedEitherDirection(accountId, publisher.AccountId.Value);
            if (isBlocked)
                throw new InvalidOperationException("You cannot follow a publisher you have blocked or who has blocked you.");
        }

        var existingRequest = await GetFollowRequest(publisherId, accountId);
        if (existingRequest != null)
        {
            if (existingRequest.State == PublisherSubscriptionState.Pending && existingRequest.EndedAt == null)
                throw new InvalidOperationException("Follow request already pending");
            if (existingRequest.State == PublisherSubscriptionState.Accepted && existingRequest.EndedAt == null)
                throw new InvalidOperationException("Already following");
            existingRequest.State = PublisherSubscriptionState.Pending;
            existingRequest.EndedAt = null;
            existingRequest.EndReason = null;
            existingRequest.EndedByAccountId = null;
            existingRequest.IsBlocking = false;
            existingRequest.ReviewedAt = null;
            existingRequest.ReviewedByAccountId = null;
            existingRequest.RejectReason = null;
            await db.SaveChangesAsync();
            return existingRequest;
        }

        var request = new SnPublisherSubscription
        {
            PublisherId = publisherId,
            AccountId = accountId,
            State = PublisherSubscriptionState.Pending
        };
        db.PublisherSubscriptions.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    public async Task<SnPublisherSubscription> ApproveFollowRequest(Guid requestId, Guid reviewerAccountId)
    {
        var request = await db.PublisherSubscriptions
            .Include(r => r.Publisher)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            throw new InvalidOperationException("Follow request not found");

        if (request.State != PublisherSubscriptionState.Pending)
            throw new InvalidOperationException("Request is not pending");

        request.State = PublisherSubscriptionState.Accepted;
        request.FollowedAt = SystemClock.Instance.GetCurrentInstant();
        request.ReviewedAt = SystemClock.Instance.GetCurrentInstant();
        request.ReviewedByAccountId = reviewerAccountId;
        request.EndedAt = null;
        request.EndReason = null;
        request.EndedByAccountId = null;

        await db.SaveChangesAsync();

        return request;
    }

    public async Task<SnPublisherSubscription> RejectFollowRequest(Guid requestId, Guid reviewerAccountId, string? reason = null)
    {
        var request = await db.PublisherSubscriptions
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            throw new InvalidOperationException("Follow request not found");

        if (request.State != PublisherSubscriptionState.Pending)
            throw new InvalidOperationException("Request is not pending");

        request.State = PublisherSubscriptionState.Rejected;
        request.ReviewedAt = SystemClock.Instance.GetCurrentInstant();
        request.ReviewedByAccountId = reviewerAccountId;
        request.RejectReason = reason;
        await db.SaveChangesAsync();

        return request;
    }

    public async Task<List<SnPublisherSubscription>> GetPendingFollowRequests(Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Where(r => r.PublisherId == publisherId && r.State == PublisherSubscriptionState.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> CleanupExpiredFollowRequests()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expirationThreshold = now.Minus(Duration.FromDays(7));

        var expiredRequests = await db.PublisherSubscriptions
            .Where(r => r.State == PublisherSubscriptionState.Pending && r.CreatedAt < expirationThreshold)
            .ToListAsync();

        if (expiredRequests.Count == 0)
            return 0;

        db.PublisherSubscriptions.RemoveRange(expiredRequests);
        await db.SaveChangesAsync();

        return expiredRequests.Count;
    }

    public async Task CancelFollowRequest(Guid publisherId, Guid accountId)
    {
        var request = await db.PublisherSubscriptions
            .FirstOrDefaultAsync(r =>
                r.PublisherId == publisherId &&
                r.AccountId == accountId
            );

        if (request != null)
        {
            db.PublisherSubscriptions.Remove(request);
            await db.SaveChangesAsync();
        }
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

    public async Task<List<SnPublisherMember>> HydrateMemberPublisherRealms(ICollection<SnPublisherMember> members)
    {
        var publishers = members.Select(m => m.Publisher).Where(p => p != null).ToList();
        if (publishers.Count == 0) return members.ToList();

        await HydratePublisherRealm(publishers);
        return members.ToList();
    }

    public async Task<List<SnPublisher>> LoadIndividualPublisherAccounts(ICollection<SnPublisher> publishers)
    {
        var accountIds = publishers
            .Where(p => p is { AccountId: not null, Type: PublisherType.Individual })
            .Select(p => p.AccountId!.Value)
            .ToList();
        if (accountIds.Count == 0) return publishers.ToList();

        var accounts = (await remoteAccounts.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        // Use LINQ to avoid explicit foreach loop and potential object allocation
        return publishers.Select(p =>
        {
            if (p.AccountId.HasValue && accounts.TryGetValue(p.AccountId.Value, out var account))
                p.Account = SnAccount.FromProtoValue(account);
            return p;
        }).ToList();
    }

    public async Task<List<SnPublisher>> HydratePublisherRealm(ICollection<SnPublisher> publishers)
    {
        var realmIds = publishers
            .Where(p => p.RealmId.HasValue)
            .Select(p => p.RealmId!.Value)
            .Distinct()
            .ToList();

        if (realmIds.Count == 0) return publishers.ToList();

        var realms = await remoteRealms.GetRealmBatch(realmIds.Select(id => id.ToString()).ToList());
        var realmDict = realms.ToDictionary(r => r.Id);

        return publishers.Select(p =>
        {
            if (p.RealmId.HasValue && realmDict.TryGetValue(p.RealmId.Value, out var realm))
                p.Realm = realm;
            return p;
        }).ToList();
    }

    public async Task<List<SnPublisher>> HydratePublisherRealmIdentity(ICollection<SnPublisher> publishers)
    {
        return await HydratePublisherRealmIdentity(publishers, null);
    }

    public async Task<List<SnPublisher>> HydratePublisherRealmIdentity(
        ICollection<SnPublisher> publishers,
        ICollection<SnPost>? posts)
    {
        var hydratedPublishers = publishers.ToList();

        if (posts is not null)
        {
            var targets = new List<(SnPublisher Publisher, Guid RealmId)>();
            var visitedPostIds = new HashSet<Guid>();

            void CollectRealmPublisherTargets(SnPost? post)
            {
                if (post is null || !visitedPostIds.Add(post.Id))
                    return;

                if (
                    post.RealmId.HasValue
                    && post.Publisher is { AccountId: not null, Type: PublisherType.Individual } publisher
                )
                {
                    targets.Add((publisher, post.RealmId.Value));
                }

                CollectRealmPublisherTargets(post.RepliedPost);
                CollectRealmPublisherTargets(post.ForwardedPost);
            }

            foreach (var post in posts)
                CollectRealmPublisherTargets(post);

            if (targets.Count == 0)
                return hydratedPublishers;

            var placeholders = targets
                .Select(t => new SnRealmMember
                {
                    RealmId = t.RealmId,
                    AccountId = t.Publisher.AccountId!.Value
                })
                .DistinctBy(m => (m.RealmId, m.AccountId))
                .ToList();
            var realmMembers = await remoteRealms.LoadMemberAccounts(placeholders);
            var realmMap = realmMembers.ToDictionary(m => (m.RealmId, m.AccountId), m => m);

            foreach (var target in targets)
            {
                if (
                    !realmMap.TryGetValue(
                        (target.RealmId, target.Publisher.AccountId!.Value),
                        out var realmMember
                    )
                )
                    continue;

                target.Publisher.RealmNick = realmMember.Nick;
                target.Publisher.RealmBio = realmMember.Bio;
                target.Publisher.RealmExperience = realmMember.Experience;
                target.Publisher.RealmLevel = realmMember.Level;
                target.Publisher.RealmLevelingProgress = realmMember.LevelingProgress;
                target.Publisher.RealmLabel = realmMember.Label;
            }

            return hydratedPublishers;
        }

        var accountRealmPairs = new Dictionary<Guid, Guid>();
        foreach (var p in hydratedPublishers.Where(p =>
            p is { RealmId: not null, AccountId: not null, Type: PublisherType.Individual }))
        {
            if (!accountRealmPairs.ContainsKey(p.AccountId!.Value))
                accountRealmPairs[p.AccountId.Value] = p.RealmId!.Value;
        }

        if (accountRealmPairs.Count == 0) return hydratedPublishers;

        var fallbackPlaceholders = accountRealmPairs
            .Select(kv => new SnRealmMember { RealmId = kv.Value, AccountId = kv.Key })
            .ToList();
        var fallbackRealmMembers = await remoteRealms.LoadMemberAccounts(fallbackPlaceholders);
        var fallbackRealmMap = fallbackRealmMembers
            .ToDictionary(m => m.AccountId, m => m);

        foreach (var publisher in hydratedPublishers)
        {
            if (publisher.AccountId is null) continue;
            if (!fallbackRealmMap.TryGetValue(publisher.AccountId.Value, out var realmMember)) continue;

            publisher.RealmNick = realmMember.Nick;
            publisher.RealmBio = realmMember.Bio;
            publisher.RealmExperience = realmMember.Experience;
            publisher.RealmLevel = realmMember.Level;
            publisher.RealmLevelingProgress = realmMember.LevelingProgress;
            publisher.RealmLabel = realmMember.Label;
        }

        return hydratedPublishers;
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
            .Select(p => new { p.Id, p.PublisherId, p.AwardedScore })
            .ToListAsync();

        // Get reactions for these posts
        var postIds = postsInPeriod.Select(p => p.Id).ToList();
        var reactions = await db.PostReactions
            .Where(r => postIds.Contains(r.PostId))
            .ToListAsync();

        // Group stats by publisher id
        var postIdToPublisher = postsInPeriod
            .ToDictionary(p => p.Id, p => p.PublisherId);
        var publisherStats = postsInPeriod
            .GroupBy(p => p.PublisherId)
            .ToDictionary(g => g.Key,
                g => new
                {
                    PostCount = g.Count(), Upvotes = 0, Downvotes = 0, AwardScore = g.Sum(p => (double)p.AwardedScore)
                });

        foreach (var reaction in reactions.Where(r => r.Attitude == PostReactionAttitude.Positive))
        {
            if (!postIdToPublisher.TryGetValue(reaction.PostId, out var pubId) ||
                !publisherStats.TryGetValue(pubId, out var stat)) continue;
            stat = new { stat.PostCount, Upvotes = stat.Upvotes + 1, stat.Downvotes, stat.AwardScore };
            publisherStats[pubId] = stat;
        }

        foreach (var reaction in reactions.Where(r => r.Attitude == PostReactionAttitude.Negative))
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
                await experiences.AddRecordAsync(new DyAddExperienceRecordRequest
                {
                    Reason = localization.Get("publishingRewardTitle", receiver.Language,
                        new { publisher = $"@{publisherName}", date }),
                    ReasonType = "publishers.rewards",
                    AccountId = receiver.Id.ToString(),
                    Delta = totalExperience,
                });
            }
        }

        // Foreach loop through publishers to set social credit
        var expiredAt = now.InZone(DateTimeZone.Utc).Date.PlusDays(30).AtStartOfDayInZone(DateTimeZone.Utc)
            .Minus(Duration.FromMilliseconds(1)).ToInstant();
        foreach (var (publisherId, value) in publisherStats)
        {
            var upvotes = value.Upvotes;
            var downvotes = value.Downvotes;
            var awardScore = value.AwardScore;

            var netVotes = upvotes - downvotes;
            var points = netVotes + awardScore * 0.1;
            var socialCreditDelta = (int)(points);

            if (socialCreditDelta == 0) continue;

            if (!publisherAccounts.TryGetValue(publisherId, out var receivers) || receivers.Count == 0)
                continue;

            var publisherName = publishers.TryGetValue(publisherId, out var pub) ? pub.Name : "unknown";

            // Set social credit for receivers, expired before next settle
            foreach (var receiver in receivers)
            {
                await socialCredits.AddRecordAsync(new DyAddSocialCreditRecordRequest
                {
                    Reason = localization.Get("publishingRewardTitle", receiver.Language,
                        new { publisher = $"@{publisherName}", date }),
                    ReasonType = "publishers.rewards",
                    AccountId = receiver.Id.ToString(),
                    Delta = socialCreditDelta,
                    ExpiredAt = expiredAt.ToTimestamp(),
                });
            }
        }

        // Foreach loop through publishers to set rating
        foreach (var (publisherId, value) in publisherStats)
        {
            var upvotes = value.Upvotes;
            var downvotes = value.Downvotes;
            var awardScore = value.AwardScore;

            var netVotes = upvotes - downvotes;
            var points = netVotes + awardScore * 0.1;
            var ratingDelta = (int)points;

            if (ratingDelta == 0) continue;

            var publisherName = publishers.TryGetValue(publisherId, out var pub) ? pub.Name : "unknown";

            await ratingService.AddRecord(
                "publishers.rewards",
                localization.Get("publishingRewardTitle", "en",
                    new { publisher = $"@{publisherName}", date }),
                ratingDelta,
                publisherId
            );
        }
    }

    public async Task SettlePostAwardsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var unsettledAwards = await db.PostAwards
            .Where(a => a.SettledAt == null && a.Attitude == PostReactionAttitude.Positive)
            .Select(a => new
            {
                a.Id,
                a.Amount,
                a.PostId,
                PublisherId = a.Post.PublisherId
            })
            .ToListAsync();

        if (unsettledAwards.Count == 0)
            return;

        // Publish merchant award events for all unsettled awards with publishers
        // Wallet handles settlement creation idempotently
        var awardsWithPublisher = unsettledAwards.ToList();

        if (awardsWithPublisher.Count > 0)
        {
            foreach (var award in awardsWithPublisher)
            {
                try
                {
                    await merchantRpc.CreateMerchantSettlementAsync(
                        publisherId: award.PublisherId.ToString(),
                        currency: "points",
                        amount: award.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        awardId: award.Id.ToString());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create merchant settlement for award {AwardId}", award.Id);
                }
            }
        }

        // Mark all awards as settled
        var allIds = unsettledAwards.Select(x => x.Id).ToList();
        await db.PostAwards
            .Where(a => allIds.Contains(a.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SettledAt, (Instant?)now));

        logger.LogInformation(
            "SettlePostAwardsAsync: marked {Count} awards as settled, {WithPublisher} with publisher",
            unsettledAwards.Count, awardsWithPublisher.Count);
    }

    public async Task<Dictionary<Guid, double>> AggressiveResettle(
        Instant dateFrom,
        Instant dateTo,
        Guid? publisherId = null
    )
    {
        var publisherStats = new Dictionary<Guid, dynamic>();

        var currentDate = dateFrom.InZone(DateTimeZone.Utc).Date;
        var endDate = dateTo.InZone(DateTimeZone.Utc).Date;

        while (currentDate <= endDate)
        {
            var periodStart = currentDate.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
            var periodEnd = periodStart.Plus(Duration.FromDays(1)).Minus(Duration.FromMilliseconds(1));

            var postsInPeriod = await db.Posts
                .Where(p => p.CreatedAt >= periodStart && p.CreatedAt <= periodEnd)
                .Where(p => publisherId == null || p.PublisherId == publisherId)
                .Select(p => new { Id = p.Id, PublisherId = p.PublisherId, AwardedScore = p.AwardedScore })
                .ToListAsync();

            var postIds = postsInPeriod.Select(p => p.Id).ToList();
            var reactions = await db.PostReactions
                .Where(r => postIds.Contains(r.PostId))
                .ToListAsync();

            var postIdToPublisher = postsInPeriod
                .ToDictionary(p => p.Id, p => p.PublisherId);

            var dayStats = postsInPeriod
                .GroupBy(p => p.PublisherId)
                .ToDictionary(g => g.Key,
                    g => new
                    {
                        PostCount = g.Count(),
                        Upvotes = 0,
                        Downvotes = 0,
                        AwardScore = g.Sum(p => (double)p.AwardedScore)
                    });

            foreach (var reaction in reactions.Where(r => r.Attitude == Shared.Models.PostReactionAttitude.Positive))
            {
                if (!postIdToPublisher.TryGetValue(reaction.PostId, out var pubId) ||
                    !dayStats.TryGetValue(pubId, out var stat)) continue;
                stat = new { stat.PostCount, Upvotes = stat.Upvotes + 1, stat.Downvotes, stat.AwardScore };
                dayStats[pubId] = stat;
            }

            foreach (var reaction in reactions.Where(r => r.Attitude == Shared.Models.PostReactionAttitude.Negative))
            {
                if (!postIdToPublisher.TryGetValue(reaction.PostId, out var pubId) ||
                    !dayStats.TryGetValue(pubId, out var stat)) continue;
                stat = new { stat.PostCount, stat.Upvotes, Downvotes = stat.Downvotes + 1, stat.AwardScore };
                dayStats[pubId] = stat;
            }

            foreach (var (pubId, stat) in dayStats)
            {
                if (!publisherStats.ContainsKey(pubId))
                {
                    publisherStats[pubId] = new
                    {
                        PostCount = 0, Upvotes = 0, Downvotes = 0, AwardScore = 0.0
                    };
                }

                var existing = publisherStats[pubId];
                publisherStats[pubId] = new
                {
                    PostCount = existing.PostCount + stat.PostCount,
                    Upvotes = existing.Upvotes + stat.Upvotes,
                    Downvotes = existing.Downvotes + stat.Downvotes,
                    AwardScore = existing.AwardScore + stat.AwardScore
                };
            }

            currentDate = currentDate.PlusDays(1);
        }

        var results = new Dictionary<Guid, double>();

        foreach (var (pubId, stat) in publisherStats)
        {
            var upvotes = stat.Upvotes;
            var downvotes = stat.Downvotes;
            var awardScore = stat.AwardScore;

            var netVotes = upvotes - downvotes;
            var points = netVotes + awardScore * 0.1;
            var ratingDelta = (int)points;

            if (ratingDelta == 0) continue;

            var publisher = await db.Publishers
                .Where(p => p.Id == pubId)
                .Select(p => new { p.Name })
                .FirstOrDefaultAsync();

            var publisherName = publisher?.Name ?? "unknown";

            await ratingService.AddRecord(
                "publishers.resettle",
                $"Resettle {dateFrom.InZone(DateTimeZone.Utc).Date:yyyy-MM-dd} to {dateTo.InZone(DateTimeZone.Utc).Date:yyyy-MM-dd}",
                ratingDelta,
                pubId
            );

            results[pubId] = ratingDelta;
        }

        return results;
    }

    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    public async Task<SnPublisher?> EnableFediverseAsync(Guid publisherId, Guid requesterAccountId)
    {
        var member = await db.PublisherMembers
            .Where(m => m.PublisherId == publisherId && m.AccountId == requesterAccountId)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < PublisherMemberRole.Manager)
            throw new UnauthorizedAccessException(
                "You need at least Manager role to enable fediverse for this publisher");

        var publisher = await db.Publishers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == publisherId);

        if (publisher == null)
            throw new InvalidOperationException("Publisher not found");

        if (publisher.DeletedAt != null)
        {
            logger.LogInformation("Reactivating soft-deleted fediverse actor for publisher: {PublisherId}", publisherId);
            publisher.DeletedAt = null;
            publisher.LastActivityAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
            return publisher;
        }

        if (publisher.Uri != null)
            throw new InvalidOperationException("Fediverse actor already exists for this publisher");

        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain == Domain);

        if (instance == null)
        {
            instance = new SnFediverseInstance
            {
                Domain = Domain,
                Name = Domain
            };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var (privateKey, publicKey) = HttpSignature.GenerateKeyPair();

        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";

        publisher.Uri = actorUrl;
        publisher.ActorType = "Person";
        publisher.Username = publisher.Name;
        publisher.InstanceId = instance.Id;
        publisher.InstanceDomain = Domain;
        publisher.InboxUri = $"{actorUrl}/inbox";
        publisher.OutboxUri = $"{actorUrl}/outbox";
        publisher.FollowersUri = $"{actorUrl}/followers";
        publisher.FollowingUri = $"{actorUrl}/following";
        publisher.FeaturedUri = $"{actorUrl}/featured";
        publisher.PublicKeyId = $"{actorUrl}#main-key";
        publisher.PublicKey = publicKey;
        publisher.AvatarUrl = publisher.Picture != null ? $"{assetsBaseUrl}/{publisher.Picture.Id}" : null;
        publisher.HeaderUrl = publisher.Background != null ? $"{assetsBaseUrl}/{publisher.Background.Id}" : null;
        publisher.LastActivityAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();

        var fediverseKey = new SnFediverseKey
        {
            KeyId = $"{actorUrl}#main-key",
            KeyPem = publicKey,
            PrivateKeyPem = privateKey,
            PublisherId = publisher.Id,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        db.FediverseKeys.Add(fediverseKey);
        await db.SaveChangesAsync();

        return publisher;
    }

    public async Task<bool> DisableFediverseAsync(Guid publisherId, Guid requesterAccountId)
    {
        var member = await db.PublisherMembers
            .Where(m => m.PublisherId == publisherId && m.AccountId == requesterAccountId)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < PublisherMemberRole.Manager)
            throw new UnauthorizedAccessException(
                "You need at least Manager role to disable fediverse for this publisher");

        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Id == publisherId);

        if (publisher == null || publisher.Uri == null)
            return true;

        publisher.Uri = null;
        publisher.ActorType = null;
        publisher.Username = null;
        publisher.InstanceId = null;
        publisher.InstanceDomain = null;
        publisher.InboxUri = null;
        publisher.OutboxUri = null;
        publisher.FollowersUri = null;
        publisher.FollowingUri = null;
        publisher.FeaturedUri = null;
        publisher.PublicKeyId = null;
        publisher.PublicKey = null;
        publisher.AvatarUrl = null;
        publisher.HeaderUrl = null;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<FediverseStatus?> GetFediverseStatusAsync(Guid publisherId, Guid? requesterAccountId = null)
    {
        var actor = await db.Publishers
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == publisherId && a.Uri != null);

        var followerCount = await db.PublisherSubscriptions
            .Where(r => r.PublisherId == publisherId
                && r.State == PublisherSubscriptionState.Accepted
                && r.EndedAt == null
                && !r.IsBlocking)
            .CountAsync();

        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Id == publisherId);

        if (publisher == null)
            return null;

        return new FediverseStatus
        {
            Enabled = actor != null,
            Actor = actor,
            FollowerCount = followerCount,
            ActorUri = actor?.Uri
        };
    }

    public async Task<SnPublisher?> GetLocalActorAsync(Guid publisherId)
    {
        return await db.Publishers
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == publisherId && a.Uri != null);
    }
}
