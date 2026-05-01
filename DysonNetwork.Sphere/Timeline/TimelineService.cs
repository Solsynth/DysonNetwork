using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using PostVisibility = DysonNetwork.Shared.Models.PostVisibility;

namespace DysonNetwork.Sphere.Timeline;

public class TimelineService(
    AppDatabase db,
    Publisher.PublisherService pub,
    Post.PostService ps,
    RemoteRealmService rs,
    DyProfileService.DyProfileServiceClient accounts,
    RemoteAccountService remoteAccounts,
    DysonNetwork.Shared.Cache.ICacheService cache,
    Automod.AutomodService automodService,
    ILogger<TimelineService> logger
)
{
    private const double ArticleTypeBoost = 1.5d;
    private const double RealmBoostLevelRankBonus = 0.6d;
    private const double PublisherRepeatPenalty = 1.35d;
    private const double ExplicitPositiveFeedbackScore = 4d;
    private const double ExplicitNegativeFeedbackScore = -30d;
    private const double PersonalizedLowRankThresholdFloor = 0.35d;
    private const double PersonalizedLowRankThresholdRatio = 0.18d;
    private const int DiscoveryCandidatePostTake = 48;
    private const int TimelineCandidateMultiplier = 2;
    private const int RecentServedPostLimit = 100;
    private const double AutomatedPostPenalty = 2.5d;
    private const double SubscriptionBoostBonus = 1.5d;
    private static readonly TimeSpan DiscoveryProfileCacheTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FriendIdsCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UserRealmsCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecentServedPostsCacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan SoftCursorCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly Duration DiscoveryLookback = Duration.FromDays(45);

    private static double CalculateBaseRank(SnPost post, Instant now)
    {
        var performanceScore =
            post.ReactionScore * 1.4 + post.ThreadRepliesCount * 0.8 + (double)post.AwardedScore / 10d;
        if (post.Type == PostType.Article)
            performanceScore += ArticleTypeBoost;
        if (post.Realm is not null && post.Realm.BoostLevel > 0)
            performanceScore += post.Realm.BoostLevel * RealmBoostLevelRankBonus;

        var postTime = post.PublishedAt ?? post.CreatedAt;
        var timeScore = (now - postTime).TotalMinutes;
        var performanceWeight = performanceScore + 5;
        var normalizedTime = timeScore / 60.0;
        return performanceWeight / Math.Pow(normalizedTime + 1.0, 1.2);
    }

    public async Task<SnTimelinePage> ListEventsForAnyone(
        int take,
        Instant? cursor,
        SnTimelineMode mode
    )
    {
        var activities = new List<SnTimelineEvent>();

        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();

        var gatekeptPublisherIds = await GetGatekeptPublisherIds(publicRealmIds);

        var postsQuery = BuildPostsQuery(cursor, null, publicRealmIds)
            .FilterWithVisibility(null, [], [], isListing: true, gatekeptPublisherIds)
            .Take(take * TimelineCandidateMultiplier);

        var posts = await GetAndProcessPosts(postsQuery);
        await LoadPostsRealmsAsync(posts, rs);
        posts = await RankPosts(posts, take, null, mode);

        var interleaved = new List<SnTimelineEvent>();
        var random = new Random();
        foreach (var post in posts)
        {
            if (random.NextDouble() < 0.15)
            {
                var discovery = await MaybeGetDiscoveryActivity();
                if (discovery != null)
                    interleaved.Add(discovery);
            }

            interleaved.Add(post.ToActivity());
        }

        activities.AddRange(interleaved);

        if (activities.Count == 0)
            activities.Add(SnTimelineEvent.Empty());

        return BuildTimelinePage(activities, posts, mode);
    }

    private async Task<HashSet<Guid>> GetGatekeptPublisherIds(List<Guid> realmIds)
    {
        var publisherIds = await db.Publishers
            .Where(p => p.RealmId == null || realmIds.Contains(p.RealmId.Value))
            .Select(p => p.Id)
            .ToListAsync();

        var gatekeptPublisherIds = new HashSet<Guid>();
        foreach (var publisherId in publisherIds)
        {
            if (await pub.HasPostsRequireFollowFlag(publisherId))
            {
                gatekeptPublisherIds.Add(publisherId);
            }
        }

        return gatekeptPublisherIds;
    }

    public async Task<SnTimelinePage> ListEvents(
        int take,
        Instant? cursor,
        DyAccount currentUser,
        SnTimelineMode mode,
        string? filter = null,
        bool aggressive = true
    )
    {
        var activities = new List<SnTimelineEvent>();

        var accountId = Guid.Parse(currentUser.Id);

        Instant? effectiveCursor = cursor.HasValue
            ? cursor
            : null;

        var userFriends = await GetCachedFriendIds(accountId, currentUser.Id);
        var userPublishers = await pub.GetUserPublishers(accountId);
        var userPublisherIds = userPublishers.Select(x => x.Id).ToList();

        var filteredPublishers = await GetFilteredPublishers(filter, currentUser, userFriends);
        var filteredPublishersId = filteredPublishers?.Select(e => e.Id).ToList();

        var userRealms = await GetCachedUserRealms(accountId);

        var boostedPostIds = await GetBoostedPostIdsForTimelineAsync(accountId, userPublishers, effectiveCursor);

        // Get visible fediverse actor IDs based on user's follows and friends' follows
        var visibleFediverseActorIds = await GetVisibleFediverseActorIdsAsync(accountId, userFriends);

        logger.LogInformation(
            "ListEvents: account={AccountId}, mode={Mode}, filter={Filter}, cursor={Cursor}, effectiveCursor={EffectiveCursor}, userRealms={RealmCount}, boostedPosts={BoostedCount}, visibleFediverseActors={FediverseActorCount}",
            accountId, mode, filter, cursor, effectiveCursor, userRealms.Count, boostedPostIds.Count, visibleFediverseActorIds.Count
        );

        var postsQuery = BuildPostsQuery(effectiveCursor, filteredPublishersId, userRealms);
        postsQuery = postsQuery.Where(p => p.FediverseUri == null || (p.ActorId.HasValue && visibleFediverseActorIds.Contains(p.ActorId.Value)));

        var timelinePublishers = filter is null ? userPublishers : [];

        HashSet<Guid> gatekeptPublisherIds = await GetGatekeptPublisherIds(userRealms);
        HashSet<Guid> followerPublisherIds = [];

        if (gatekeptPublisherIds.Count > 0)
        {
            var activeSubscriptions = await db.PublisherSubscriptions
                .Where(s => s.AccountId == accountId && s.EndedAt == null)
                .Select(s => s.PublisherId)
                .ToListAsync();
            followerPublisherIds = activeSubscriptions.ToHashSet();
        }

        postsQuery = postsQuery
            .FilterWithVisibility(
                currentUser,
                userFriends,
                timelinePublishers,
                isListing: true,
                gatekeptPublisherIds,
                followerPublisherIds
            )
            .Take(take * TimelineCandidateMultiplier);

        var posts = await GetAndProcessPosts(postsQuery, currentUser);

        logger.LogInformation("ListEvents: fetched {PostCount} posts before ranking", posts.Count);

        var existingPostIds = posts.Select(p => p.Id).ToHashSet();
        var newBoostedPostIds = boostedPostIds.Where(id => !existingPostIds.Contains(id)).ToList();

        if (newBoostedPostIds.Count > 0)
        {
            var boostedPostsQuery = db.Posts
                .Include(p => p.RepliedPost)
                .Include(p => p.ForwardedPost)
                .Include(p => p.Categories)
                .Include(p => p.Tags)
                .Include(p => p.FeaturedRecords)
                .Where(p => newBoostedPostIds.Contains(p.Id))
                .Where(p => p.DraftedAt == null)
                .Where(p => cursor == null || p.PublishedAt < cursor);

            var boostedPosts = await GetAndProcessPosts(boostedPostsQuery, currentUser);
            posts.AddRange(boostedPosts);
            logger.LogInformation("ListEvents: added {BoostedCount} boosted posts to timeline", boostedPosts.Count);
        }

        await LoadPostsRealmsAsync(posts, rs);

        posts = await RankPosts(posts, take, currentUser, mode, aggressive);
        await RememberServedPostsAsync(posts, accountId);

        logger.LogInformation("ListEvents: returning {PostCount} posts after ranking", posts.Count);

        await UpdateSoftCursorAsync(accountId);

        var interleaved = new List<SnTimelineEvent>();
        SnTimelineEvent? personalizedDiscovery = null;
        var discoveryInsertIndex = cursor == null && posts.Count >= 4 ? Math.Min(4, posts.Count) : -1;
        for (var i = 0; i < posts.Count; i++)
        {
            var post = posts[i];
            if (i == discoveryInsertIndex)
            {
                personalizedDiscovery ??= await MaybeGetDiscoveryActivity(
                    currentUser,
                    userFriends,
                    userPublisherIds,
                    userRealms
                );
                var discovery = personalizedDiscovery;
                if (discovery != null)
                    interleaved.Add(discovery);
            }

            interleaved.Add(post.ToActivity());
        }

        activities.AddRange(interleaved);

        if (activities.Count == 0)
            activities.Add(SnTimelineEvent.Empty());

        return BuildTimelinePage(activities, posts, mode);
    }

    private async Task<List<Guid>> GetBoostedPostIdsForTimelineAsync(
        Guid accountId,
        List<SnPublisher> userPublishers,
        Instant? cursor
    )
    {
        var publisherIds = userPublishers.Select(p => p.Id).ToList();

        var localActorIds = await db.FediverseActors
            .Where(a => a.PublisherId != null && publisherIds.Contains(a.PublisherId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        if (localActorIds.Count == 0)
            return new List<Guid>();

        var followedActorIds = await db.FediverseRelationships
            .Where(r => localActorIds.Contains(r.ActorId) && r.State == RelationshipState.Accepted)
            .Select(r => r.TargetActorId)
            .ToListAsync();

        if (followedActorIds.Count == 0)
            return new List<Guid>();

        var query = db.Boosts
            .Where(b => followedActorIds.Contains(b.ActorId))
            .Where(b => cursor == null || b.BoostedAt < cursor)
            .OrderByDescending(b => b.BoostedAt)
            .Select(b => b.PostId);

        return await query.Distinct().ToListAsync();
    }

    private async Task<HashSet<Guid>> GetVisibleFediverseActorIdsAsync(
        Guid accountId,
        List<Guid> userFriendIds
    )
    {
        var visibleActorIds = new HashSet<Guid>();

        // 1. Get local actors for the current user's publishers
        var userPublisherIds = await db.Publishers
            .Where(p => p.AccountId == accountId)
            .Select(p => p.Id)
            .ToListAsync();

        var userLocalActorIds = await db.FediverseActors
            .Where(a => a.PublisherId != null && userPublisherIds.Contains(a.PublisherId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        // 2. Get actors followed by current user's local actors
        if (userLocalActorIds.Count > 0)
        {
            var followedByUser = await db.FediverseRelationships
                .Where(r => userLocalActorIds.Contains(r.ActorId) && r.State == RelationshipState.Accepted)
                .Select(r => r.TargetActorId)
                .ToListAsync();
            foreach (var id in followedByUser)
                visibleActorIds.Add(id);
        }

        // 3. Get local actors for friends
        var friendPublisherIds = await db.Publishers
            .Where(p => p.AccountId.HasValue && userFriendIds.Contains(p.AccountId.Value))
            .Select(p => p.Id)
            .ToListAsync();

        var friendLocalActorIds = await db.FediverseActors
            .Where(a => a.PublisherId != null && friendPublisherIds.Contains(a.PublisherId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        // 4. Get actors followed by friends' local actors
        if (friendLocalActorIds.Count > 0)
        {
            var followedByFriends = await db.FediverseRelationships
                .Where(r => friendLocalActorIds.Contains(r.ActorId) && r.State == RelationshipState.Accepted)
                .Select(r => r.TargetActorId)
                .ToListAsync();
            foreach (var id in followedByFriends)
                visibleActorIds.Add(id);
        }

        return visibleActorIds;
    }

    private async Task UpdateSoftCursorAsync(Guid accountId)
    {
        var cacheKey = $"timeline:soft-cursor:{accountId}";
        var now = SystemClock.Instance.GetCurrentInstant();
        await cache.SetAsync(cacheKey, now, SoftCursorCacheTtl);
    }

    private async Task<SnTimelineEvent?> MaybeGetDiscoveryActivity()
    {
        var options = new List<Func<Task<SnTimelineEvent?>>>();
        if (Random.Shared.NextDouble() < 0.5)
            options.Add(() => GetRealmDiscoveryActivity());
        if (Random.Shared.NextDouble() < 0.5)
            options.Add(() => GetPublisherDiscoveryActivity());
        if (Random.Shared.NextDouble() < 0.5)
            options.Add(() => GetShuffledPostsActivity());
        if (options.Count == 0)
            return null;
        var random = new Random();
        var pick = options[random.Next(options.Count)];
        return await pick();
    }

    private async Task<SnTimelineEvent?> MaybeGetDiscoveryActivity(
        DyAccount currentUser,
        List<Guid> userFriends,
        List<Guid> userPublisherIds,
        List<Guid> userRealms
    )
    {
        var profile = await GetDiscoveryProfile(
            currentUser,
            userFriends,
            userPublisherIds,
            userRealms
        );

        var options = new List<Func<SnTimelineEvent?>>();
        if (profile.SuggestedPublishers.Count > 0)
        {
            var pickedPublisher = PickDiscoverySuggestion(profile.SuggestedPublishers);
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "publisher",
                "Suggested publisher",
                [pickedPublisher]
            ).ToActivity());
        }

        if (profile.SuggestedAccounts.Count > 0)
        {
            var pickedAccount = PickDiscoverySuggestion(profile.SuggestedAccounts);
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "account",
                "People you may know",
                [pickedAccount]
            ).ToActivity());
        }

        if (profile.SuggestedRealms.Count > 0)
        {
            var pickedRealm = PickDiscoverySuggestion(profile.SuggestedRealms);
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "realm",
                "Suggested realm",
                [pickedRealm]
            ).ToActivity());
        }

        if (options.Count == 0)
            return await MaybeGetDiscoveryActivity();

        return options[Random.Shared.Next(options.Count)]();
    }

    private async Task<List<SnPost>> RankPosts(
        List<SnPost> posts,
        int take,
        DyAccount? currentUser = null,
        SnTimelineMode mode = SnTimelineMode.Personalized,
        bool aggressive = true
    )
    {
        if (mode == SnTimelineMode.Latest)
            return SortLatestPosts(posts, take);

        var now = SystemClock.Instance.GetCurrentInstant();
        var recentServedPenalty = currentUser is null
            ? new Dictionary<Guid, double>()
            : await GetRecentServedPenaltyMap(Guid.Parse(currentUser.Id), posts, now);
        var personalizationBonus = mode != SnTimelineMode.Personalized || currentUser is null
            ? new Dictionary<Guid, double>()
            : await GetPersonalizationBonusMap(posts, Guid.Parse(currentUser.Id), now);
        var publisherRatingBonus = await GetPublisherRatingBonusMap(posts);
        var automatedPenalty = await GetAutomatedPenaltyMap(posts);
        var subscriptionBoost = currentUser is null
            ? new Dictionary<Guid, double>()
            : await GetSubscriptionBoostMap(posts, Guid.Parse(currentUser.Id));
        var shadowbanStatus = await GetShadowbanStatusMap(posts);
        var automodPenalties = await automodService.GetAutomodPenaltiesAsync(posts);

        const double PersonalizationBoostMultiplier = 3.0d;

        logger.LogDebug(
            "RankPosts: mode={Mode}, posts={PostCount}, personalizationBonus entries={PersonalizationCount}",
            mode, posts.Count, personalizationBonus.Count
        );

        var rankedCandidates = posts
            .Select(p =>
            {
                var (automodPenaltyValue, shouldHide) = automodPenalties.GetValueOrDefault(p.Id, (0d, false));
                var (isShadowbanned, isShadowbannedForListing) = shadowbanStatus.GetValueOrDefault(p.Id, (false, false));
                var persoBonus = personalizationBonus.GetValueOrDefault(p.Id, 0d) * PersonalizationBoostMultiplier;
                var baseRank = CalculateBaseRank(p, now)
                    - recentServedPenalty.GetValueOrDefault(p.Id, 0d)
                    + persoBonus
                    + publisherRatingBonus.GetValueOrDefault(p.Id, 0d)
                    - automatedPenalty.GetValueOrDefault(p.Id, 0d)
                    + subscriptionBoost.GetValueOrDefault(p.Id, 0d)
                    - automodPenaltyValue;

                logger.LogTrace(
                    "Post {PostId}: baseRank={BaseRank}, persoBonus={PersoBonus}, shadowbanForListing={ShadowbanForListing}, automodPenalty={AutomodPenalty}, finalRank={FinalRank}, shouldHide={ShouldHide}",
                    p.Id, CalculateBaseRank(p, now), persoBonus, isShadowbannedForListing, automodPenaltyValue, baseRank, shouldHide || isShadowbannedForListing
                );

                return new RankedPostCandidate
                {
                    Post = p,
                    Rank = baseRank,
                    ShouldHide = shouldHide || isShadowbannedForListing
                };
            })
            .Where(x => !x.ShouldHide)
            .OrderByDescending(x => x.Rank)
            .ToList();

        logger.LogInformation(
            "RankPosts: mode={Mode}, inputPosts={InputCount}, afterRanking={RankedCount}, afterHiding={HiddenCount}",
            mode, posts.Count, rankedCandidates.Count, posts.Count - rankedCandidates.Count
        );

        if (mode == SnTimelineMode.Personalized && currentUser is not null && aggressive)
            rankedCandidates = FilterLowRankPersonalizedCandidates(rankedCandidates, take);

        var diversified = DiversifyRankedPosts(rankedCandidates, take);
        logger.LogInformation(
            "RankPosts: after diversification, result={ResultCount}, top5Ranks=[{Ranks}]",
            diversified.Count,
            string.Join(",", diversified.Take(5).Select(p => p.DebugRank.ToString("F2")))
        );
        return diversified;
    }

    private static List<RankedPostCandidate> FilterLowRankPersonalizedCandidates(
        List<RankedPostCandidate> rankedCandidates,
        int take
    )
    {
        if (rankedCandidates.Count <= take)
            return rankedCandidates;

        var strongestRank = rankedCandidates[0].Rank;
        var cutoff = Math.Max(
            PersonalizedLowRankThresholdFloor,
            strongestRank * PersonalizedLowRankThresholdRatio
        );

        var filteredCandidates = rankedCandidates
            .Where(x => x.Rank >= cutoff)
            .ToList();

        if (filteredCandidates.Count >= Math.Min(take, 3))
            return filteredCandidates;

        return rankedCandidates
            .Take(Math.Min(rankedCandidates.Count, Math.Max(take, 3)))
            .ToList();
    }

    private async Task<Dictionary<Guid, double>> GetPersonalizationBonusMap(
        List<SnPost> posts,
        Guid accountId,
        Instant now
    )
    {
        if (posts.Count == 0)
            return [];

        var tagIds = posts.SelectMany(p => p.Tags.Select(x => x.Id)).Distinct().ToList();
        var categoryIds = posts.SelectMany(p => p.Categories.Select(x => x.Id)).Distinct().ToList();
        var publisherIds = posts.Where(p => p.PublisherId.HasValue).Select(p => p.PublisherId!.Value).Distinct().ToList();

        var interestProfiles = await db.PostInterestProfiles.Where(p => p.AccountId == accountId)
            .Where(p =>
                (p.Kind == PostInterestKind.Tag && tagIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Category && categoryIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Publisher && publisherIds.Contains(p.ReferenceId))
            )
            .ToListAsync();

        var subscriptions = await db.PostCategorySubscriptions.Where(p => p.AccountId == accountId)
            .Where(p =>
                (p.TagId.HasValue && tagIds.Contains(p.TagId.Value))
                || (p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value))
            )
            .ToListAsync();

        var tagInterest = interestProfiles.Where(x => x.Kind == PostInterestKind.Tag)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));
        var categoryInterest = interestProfiles.Where(x => x.Kind == PostInterestKind.Category)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));
        var publisherInterest = interestProfiles.Where(x => x.Kind == PostInterestKind.Publisher)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));
        var subscribedTagIds = subscriptions.Where(x => x.TagId.HasValue).Select(x => x.TagId!.Value).ToHashSet();
        var subscribedCategoryIds = subscriptions.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToHashSet();

        return posts.ToDictionary(
            post => post.Id,
            post =>
            {
                var bonus = 0d;
                bonus += post.Tags.Sum(tag => tagInterest.GetValueOrDefault(tag.Id, 0d) * 0.8d);
                bonus += post.Categories.Sum(category => categoryInterest.GetValueOrDefault(category.Id, 0d) * 0.75d);
                if (post.PublisherId.HasValue)
                {
                    var publisherScore = publisherInterest.GetValueOrDefault(post.PublisherId.Value, 0d);
                    bonus += publisherScore >= 0d
                        ? Math.Min(2d, publisherScore * 0.2d)
                        : Math.Max(-6d, publisherScore * 0.5d);
                }
                bonus += post.Tags.Count(tag => subscribedTagIds.Contains(tag.Id)) * 1.25d;
                bonus += post.Categories.Count(category => subscribedCategoryIds.Contains(category.Id)) * 1.5d;
                return bonus;
            }
        );
    }

    private static double GetDecayedInterestScore(SnPostInterestProfile profile, Instant now)
    {
        if (!profile.LastInteractedAt.HasValue)
            return profile.Score;

        var ageDays = Math.Max(0, (now - profile.LastInteractedAt.Value).TotalDays);
        var decay = Math.Exp(-ageDays / 30d);
        return profile.Score * decay;
    }

    private async Task<Dictionary<Guid, double>> GetPublisherRatingBonusMap(List<SnPost> posts)
    {
        var publisherIds = posts
            .Where(p => p.PublisherId.HasValue)
            .Select(p => p.PublisherId!.Value)
            .Distinct()
            .ToList();

        if (publisherIds.Count == 0)
            return [];

        var publishers = await db.Publishers
            .Where(p => publisherIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Rating })
            .ToDictionaryAsync(p => p.Id, p => p.Rating);

        return posts.ToDictionary(
            p => p.Id,
            p =>
            {
                if (!p.PublisherId.HasValue)
                    return 0d;

                var rating = publishers.GetValueOrDefault(p.PublisherId.Value, 100);
                var ratingLevel = rating < 100 ? -1 : rating < 200 ? 0 : rating < 300 ? 1 : 2;
                return Math.Min(3d, ratingLevel * 0.05d);
            }
        );
    }

    private async Task<Dictionary<Guid, double>> GetAutomatedPenaltyMap(List<SnPost> posts)
    {
        var publisherAccounts = posts
            .Where(p => p.Publisher?.AccountId.HasValue == true)
            .Select(p => p.Publisher!.AccountId!.Value)
            .Distinct()
            .ToList();

        if (publisherAccounts.Count == 0)
            return [];

        var statuses = await remoteAccounts.GetAccountStatusBatch(publisherAccounts);
        var automatedAccountIds = statuses
            .Where(kvp => kvp.Value.IsAutomated)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        return posts.ToDictionary(
            p => p.Id,
            p =>
            {
                var accountId = p.Publisher?.AccountId;
                if (!accountId.HasValue)
                    return 0d;

                return automatedAccountIds.Contains(accountId.Value) ? AutomatedPostPenalty : 0d;
            }
        );
    }

    private async Task<Dictionary<Guid, double>> GetSubscriptionBoostMap(List<SnPost> posts, Guid accountId)
    {
        var subscribedPublisherIds = (await pub.GetSubscribedPublishers(accountId))
            .Select(x => x.Id)
            .ToHashSet();

        if (subscribedPublisherIds.Count == 0)
            return [];

        return posts.ToDictionary(
            p => p.Id,
            p => p.PublisherId.HasValue && subscribedPublisherIds.Contains(p.PublisherId.Value)
                ? SubscriptionBoostBonus
                : 0d
        );
    }

    private async Task<Dictionary<Guid, (bool IsShadowbanned, bool IsShadowbannedForListing)>> GetShadowbanStatusMap(
        List<SnPost> posts
    )
    {
        var publisherIds = posts
            .Where(p => p.PublisherId.HasValue)
            .Select(p => p.PublisherId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, bool> publisherShadowbanStatus;
        if (publisherIds.Count == 0)
        {
            publisherShadowbanStatus = [];
        }
        else
        {
            var shadowbanData = await db.Publishers
                .Where(p => publisherIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ShadowbanReason })
                .ToListAsync();
            publisherShadowbanStatus = shadowbanData.ToDictionary(
                x => x.Id,
                x => x.ShadowbanReason.HasValue && x.ShadowbanReason != PublisherShadowbanReason.None
            );
        }

        var result = posts.ToDictionary(
            p => p.Id,
            p =>
            {
                var isPublisherShadowbanned = p.PublisherId.HasValue &&
                    publisherShadowbanStatus.GetValueOrDefault(p.PublisherId.Value, false);
                var isPostShadowbanned = p.IsShadowbanned;
                var isShadowbannedForListing = isPublisherShadowbanned || isPostShadowbanned;
                return (isShadowbanned: isPublisherShadowbanned || isPostShadowbanned, isShadowbannedForListing);
            }
        );

        logger.LogDebug(
            "GetShadowbanStatusMap: posts={PostCount}, shadowbannedPublishers={ShadowbannedPubCount}, shadowbannedPosts={ShadowbannedPostCount}",
            posts.Count,
            publisherShadowbanStatus.Count(x => x.Value),
            result.Count(x => x.Value.Item1)
        );

        return result;
    }

    public async Task<SnDiscoveryProfile> GetDiscoveryProfile(DyAccount currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        var cacheKey = $"timeline:discovery-profile:{accountId}";
        var cachedProfile = await cache.GetAsync<SnDiscoveryProfile>(cacheKey);
        if (cachedProfile is not null)
            return cachedProfile;

        var userFriends = await GetCachedFriendIds(accountId, currentUser.Id);
        var userPublishers = await pub.GetUserPublishers(accountId);
        var userRealms = await GetCachedUserRealms(accountId);

        var profile = await GetDiscoveryProfile(
            currentUser,
            userFriends,
            userPublishers.Select(x => x.Id).ToList(),
            userRealms
        );
        await cache.SetAsync(cacheKey, profile, DiscoveryProfileCacheTtl);
        return profile;
    }

    private async Task<SnDiscoveryProfile> GetDiscoveryProfile(
        DyAccount currentUser,
        List<Guid> userFriends,
        List<Guid> userPublisherIds,
        List<Guid> userRealms
    )
    {
        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();
        var interestProfiles = await db.PostInterestProfiles
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.Score)
            .Take(128)
            .ToListAsync();
        var preferences = await db.DiscoveryPreferences
            .Where(x => x.AccountId == accountId)
            .Where(x => x.State == DiscoveryPreferenceState.Uninterested)
            .ToListAsync();

        var tagInterest = interestProfiles
            .Where(x => x.Kind == PostInterestKind.Tag)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));
        var categoryInterest = interestProfiles
            .Where(x => x.Kind == PostInterestKind.Category)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));
        var publisherInterest = interestProfiles
            .Where(x => x.Kind == PostInterestKind.Publisher)
            .ToDictionary(x => x.ReferenceId, x => GetDecayedInterestScore(x, now));

        var publicRealms = await rs.GetPublicRealms("date", 40);
        var visibleRealmIds = publicRealms.Select(x => x.Id).Concat(userRealms).Distinct().ToList();
        var candidatePosts = await GetDiscoveryCandidatePosts(now, visibleRealmIds);

        var interestEntries = await BuildDiscoveryInterestEntries(interestProfiles, now);
        var suggestionContext = await BuildDiscoverySuggestionContext(
            currentUser,
            userFriends,
            userPublisherIds,
            userRealms,
            preferences,
            candidatePosts,
            publicRealms,
            tagInterest,
            categoryInterest,
            publisherInterest,
            now
        );

        return new SnDiscoveryProfile
        {
            GeneratedAt = now,
            Interests = interestEntries,
            SuggestedPublishers = suggestionContext.Publishers,
            SuggestedAccounts = suggestionContext.Accounts,
            SuggestedRealms = suggestionContext.Realms,
            Suppressed = suggestionContext.Suppressed,
        };
    }

    public async Task<SnDiscoveryPreference> MarkDiscoveryPreferenceAsync(
        DyAccount currentUser,
        DiscoveryTargetKind kind,
        Guid referenceId,
        string? reason = null
    )
    {
        var accountId = Guid.Parse(currentUser.Id);
        var preference = await db.DiscoveryPreferences
            .FirstOrDefaultAsync(x =>
                x.AccountId == accountId && x.Kind == kind && x.ReferenceId == referenceId
            );

        var now = SystemClock.Instance.GetCurrentInstant();
        if (preference == null)
        {
            preference = new SnDiscoveryPreference
            {
                AccountId = accountId,
                Kind = kind,
                ReferenceId = referenceId,
            };
            db.DiscoveryPreferences.Add(preference);
        }

        preference.State = DiscoveryPreferenceState.Uninterested;
        preference.Reason = reason;
        preference.AppliedAt = now;
        preference.UpdatedAt = now;
        if (preference.CreatedAt == default)
            preference.CreatedAt = now;

        await db.SaveChangesAsync();
        await cache.RemoveAsync($"timeline:discovery-profile:{accountId}");
        return preference;
    }

    public async Task<bool> RemoveDiscoveryPreferenceAsync(
        DyAccount currentUser,
        DiscoveryTargetKind kind,
        Guid referenceId
    )
    {
        var accountId = Guid.Parse(currentUser.Id);
        var preference = await db.DiscoveryPreferences
            .FirstOrDefaultAsync(x =>
                x.AccountId == accountId && x.Kind == kind && x.ReferenceId == referenceId
            );
        if (preference == null)
            return false;

        db.DiscoveryPreferences.Remove(preference);
        await db.SaveChangesAsync();
        await cache.RemoveAsync($"timeline:discovery-profile:{accountId}");
        return true;
    }

    public async Task<int> ResetInterestProfileAsync(DyAccount currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();

        // Delete all existing interest profiles for this user
        var existingProfiles = await db.PostInterestProfiles
            .Where(p => p.AccountId == accountId)
            .ToListAsync();
        db.PostInterestProfiles.RemoveRange(existingProfiles);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var adjustments = new List<(PostInterestKind Kind, Guid ReferenceId, double ScoreDelta)>();

        // 1. Publisher subscriptions → Publisher interests (score: 4.0)
        var subscribedPublishers = await pub.GetSubscribedPublishers(accountId);
        adjustments.AddRange(subscribedPublishers.Select(p =>
            (PostInterestKind.Publisher, p.Id, 4.0d)
        ));

        // 2. Tag/Category subscriptions → interests (score: 4.0)
        var categorySubscriptions = await db.PostCategorySubscriptions
            .Where(s => s.AccountId == accountId)
            .ToListAsync();
        adjustments.AddRange(categorySubscriptions
            .Where(s => s.TagId.HasValue)
            .Select(s => (PostInterestKind.Tag, s.TagId!.Value, 4.0d))
        );
        adjustments.AddRange(categorySubscriptions
            .Where(s => s.CategoryId.HasValue)
            .Select(s => (PostInterestKind.Category, s.CategoryId!.Value, 4.0d))
        );

        // 3. Historic posts from user's publishers → interests (score: 2.0)
        var userPublishers = await pub.GetUserPublishers(accountId);
        var userPublisherIds = userPublishers.Select(p => p.Id).ToList();

        if (userPublisherIds.Count > 0)
        {
            var recent = now.Minus(DiscoveryLookback);
            var historicPosts = await db.Posts
                .AsNoTracking()
                .Include(p => p.Tags)
                .Include(p => p.Categories)
                .Where(p => p.PublisherId != null && userPublisherIds.Contains(p.PublisherId.Value))
                .Where(p => p.DraftedAt == null)
                .Where(p =>
                    (p.PublishedAt != null && p.PublishedAt >= recent)
                    || (p.PublishedAt == null && p.CreatedAt >= recent)
                )
                .ToListAsync();

            // Aggregate tags from historic posts
            var tagScores = historicPosts
                .SelectMany(p => p.Tags)
                .GroupBy(t => t.Id)
                .Select(g => (Kind: PostInterestKind.Tag, ReferenceId: g.Key, ScoreDelta: 2.0d * g.Count()))
                .ToList();
            adjustments.AddRange(tagScores);

            // Aggregate categories from historic posts
            var categoryScores = historicPosts
                .SelectMany(p => p.Categories)
                .GroupBy(c => c.Id)
                .Select(g => (Kind: PostInterestKind.Category, ReferenceId: g.Key, ScoreDelta: 2.0d * g.Count()))
                .ToList();
            adjustments.AddRange(categoryScores);

            // Add publisher interests from user's own publishers (score: 2.0)
            adjustments.AddRange(userPublisherIds.Select(id =>
                (PostInterestKind.Publisher, id, 2.0d)
            ));
        }

        // Apply all adjustments
        await ApplyInterestProfileAdjustmentsAsync(
            accountId,
            adjustments,
            "reset",
            now
        );

        // Invalidate discovery profile cache
        await cache.RemoveAsync($"timeline:discovery-profile:{accountId}");

        return adjustments.Count;
    }

    public async Task<RecommendationFeedbackResult?> ApplyRecommendationFeedbackAsync(
        DyAccount currentUser,
        string kind,
        Guid referenceId,
        RecommendationFeedbackValue feedback,
        string? reason = null,
        bool suppress = false
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(currentUser.Id);
        var baseScore = feedback == RecommendationFeedbackValue.Positive
            ? ExplicitPositiveFeedbackScore
            : ExplicitNegativeFeedbackScore;

        List<(PostInterestKind Kind, Guid ReferenceId, double ScoreDelta)> adjustments;
        switch (kind.Trim().ToLowerInvariant())
        {
            case "post":
                {
                    adjustments = await BuildPostFeedbackAdjustmentsAsync(referenceId, baseScore);
                    if (adjustments.Count == 0)
                        return null;
                    break;
                }
            case "publisher":
                adjustments = [(PostInterestKind.Publisher, referenceId, baseScore)];
                break;
            case "tag":
                adjustments = [(PostInterestKind.Tag, referenceId, baseScore)];
                break;
            case "category":
                adjustments = [(PostInterestKind.Category, referenceId, baseScore)];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        var updatedProfiles = await ApplyInterestProfileAdjustmentsAsync(
            accountId,
            adjustments,
            $"feedback:{kind.Trim().ToLowerInvariant()}:{feedback.ToString().ToLowerInvariant()}",
            now
        );

        SnDiscoveryPreference? preference = null;
        if (suppress &&
            feedback == RecommendationFeedbackValue.Negative &&
            kind.Trim().Equals("publisher", StringComparison.OrdinalIgnoreCase))
        {
            preference = await MarkDiscoveryPreferenceAsync(
                currentUser,
                DiscoveryTargetKind.Publisher,
                referenceId,
                reason
            );
        }

        return new RecommendationFeedbackResult
        {
            UpdatedProfiles = updatedProfiles,
            Preference = preference,
        };
    }

    public async Task<SnPostInterestProfile> AdjustRecommendationWeightAsync(
        DyAccount currentUser,
        PostInterestKind kind,
        Guid referenceId,
        double scoreDelta,
        int interactionCount = 1,
        string? signalType = null
    )
    {
        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();
        var profiles = await ApplyInterestProfileAdjustmentsAsync(
            accountId,
            [(kind, referenceId, scoreDelta)],
            signalType ?? $"manual:{kind.ToString().ToLowerInvariant()}",
            now,
            interactionCount
        );
        return profiles[0];
    }

    private async Task<List<(PostInterestKind Kind, Guid ReferenceId, double ScoreDelta)>>
        BuildPostFeedbackAdjustmentsAsync(Guid postId, double baseScore)
    {
        var post = await db.Posts
            .AsNoTracking()
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null)
            return [];

        var adjustments = new List<(PostInterestKind Kind, Guid ReferenceId, double ScoreDelta)>();

        if (post.PublisherId.HasValue)
            adjustments.Add((PostInterestKind.Publisher, post.PublisherId.Value, baseScore));

        var tags = post.Tags.Select(x => x.Id).Distinct().ToList();
        if (tags.Count > 0)
        {
            var tagScore = baseScore * 0.75d / tags.Count;
            adjustments.AddRange(tags.Select(tagId => (PostInterestKind.Tag, tagId, tagScore)));
        }

        var categories = post.Categories.Select(x => x.Id).Distinct().ToList();
        if (categories.Count > 0)
        {
            var categoryScore = baseScore / categories.Count;
            adjustments.AddRange(categories.Select(categoryId => (PostInterestKind.Category, categoryId, categoryScore)));
        }

        return adjustments;
    }

    private async Task<List<SnPostInterestProfile>> ApplyInterestProfileAdjustmentsAsync(
        Guid accountId,
        IEnumerable<(PostInterestKind Kind, Guid ReferenceId, double ScoreDelta)> adjustments,
        string signalType,
        Instant occurredAt,
        int interactionCount = 1
    )
    {
        var aggregatedAdjustments = adjustments
            .GroupBy(x => (x.Kind, x.ReferenceId))
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.ReferenceId,
                ScoreDelta = g.Sum(x => x.ScoreDelta),
            })
            .ToList();

        if (aggregatedAdjustments.Count == 0)
            return [];

        var tagIds = aggregatedAdjustments
            .Where(x => x.Kind == PostInterestKind.Tag)
            .Select(x => x.ReferenceId)
            .ToList();
        var categoryIds = aggregatedAdjustments
            .Where(x => x.Kind == PostInterestKind.Category)
            .Select(x => x.ReferenceId)
            .ToList();
        var publisherIds = aggregatedAdjustments
            .Where(x => x.Kind == PostInterestKind.Publisher)
            .Select(x => x.ReferenceId)
            .ToList();

        var existingProfiles = await db.PostInterestProfiles
            .Where(p => p.AccountId == accountId)
            .Where(p =>
                (p.Kind == PostInterestKind.Tag && tagIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Category && categoryIds.Contains(p.ReferenceId))
                || (p.Kind == PostInterestKind.Publisher && publisherIds.Contains(p.ReferenceId))
            )
            .ToListAsync();

        var profileMap = existingProfiles.ToDictionary(
            x => (x.Kind, x.ReferenceId),
            x => x
        );

        foreach (var adjustment in aggregatedAdjustments)
        {
            if (!profileMap.TryGetValue((adjustment.Kind, adjustment.ReferenceId), out var profile))
            {
                profile = new SnPostInterestProfile
                {
                    AccountId = accountId,
                    Kind = adjustment.Kind,
                    ReferenceId = adjustment.ReferenceId,
                };
                db.PostInterestProfiles.Add(profile);
                profileMap[(adjustment.Kind, adjustment.ReferenceId)] = profile;
            }

            profile.Score = Math.Clamp(profile.Score + adjustment.ScoreDelta, -100d, 100d);
            profile.InteractionCount += Math.Max(1, interactionCount);
            profile.LastInteractedAt = occurredAt;
            profile.LastSignalType = signalType;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existingProfiles = await db.PostInterestProfiles
                .Where(p => p.AccountId == accountId)
                .Where(p =>
                    (p.Kind == PostInterestKind.Tag && tagIds.Contains(p.ReferenceId))
                    || (p.Kind == PostInterestKind.Category && categoryIds.Contains(p.ReferenceId))
                    || (p.Kind == PostInterestKind.Publisher && publisherIds.Contains(p.ReferenceId))
                )
                .ToListAsync();

            profileMap = existingProfiles.ToDictionary(
                x => (x.Kind, x.ReferenceId),
                x => x
            );

            foreach (var adjustment in aggregatedAdjustments)
            {
                if (!profileMap.TryGetValue((adjustment.Kind, adjustment.ReferenceId), out var profile))
                {
                    continue;
                }

                profile.Score = Math.Clamp(profile.Score + adjustment.ScoreDelta, -100d, 100d);
                profile.InteractionCount += Math.Max(1, interactionCount);
                profile.LastInteractedAt = occurredAt;
                profile.LastSignalType = signalType;
            }

            await db.SaveChangesAsync();
        }

        await cache.RemoveAsync($"timeline:discovery-profile:{accountId}");

        return aggregatedAdjustments
            .Where(x => profileMap.ContainsKey((x.Kind, x.ReferenceId)))
            .Select(x => profileMap[(x.Kind, x.ReferenceId)])
            .ToList();
    }

    private async Task<List<SnPost>> GetDiscoveryCandidatePosts(Instant now, List<Guid> visibleRealmIds)
    {
        var recent = now - DiscoveryLookback;
        return await db.Posts
            .AsNoTracking()
            .Include(p => p.Tags)
            .Include(p => p.Categories)
            .Where(p => p.DraftedAt == null)
            .Where(p => p.RepliedPostId == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.PublisherId != null || p.RealmId != null)
            .Where(p =>
                (p.PublishedAt != null && p.PublishedAt >= recent)
                || (p.PublishedAt == null && p.CreatedAt >= recent)
            )
            .Where(p => p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value))
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Take(DiscoveryCandidatePostTake)
            .ToListAsync();
    }

    private async Task<List<SnDiscoveryInterestEntry>> BuildDiscoveryInterestEntries(
        List<SnPostInterestProfile> profiles,
        Instant now
    )
    {
        var tagIds = profiles
            .Where(x => x.Kind == PostInterestKind.Tag)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();
        var categoryIds = profiles
            .Where(x => x.Kind == PostInterestKind.Category)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();
        var publisherIds = profiles
            .Where(x => x.Kind == PostInterestKind.Publisher)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();

        var tags = await db.PostTags
            .AsNoTracking()
            .Where(x => tagIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name ?? x.Slug);
        var categories = await db.PostCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name ?? x.Slug);
        var publishers = await db.Publishers
            .AsNoTracking()
            .Where(x => publisherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nick);

        return profiles
            .Select(profile => new SnDiscoveryInterestEntry
            {
                Kind = profile.Kind.ToString().ToLowerInvariant(),
                ReferenceId = profile.ReferenceId,
                Label = profile.Kind switch
                {
                    PostInterestKind.Tag => tags.GetValueOrDefault(
                        profile.ReferenceId,
                        profile.ReferenceId.ToString()
                    ),
                    PostInterestKind.Category => categories.GetValueOrDefault(
                        profile.ReferenceId,
                        profile.ReferenceId.ToString()
                    ),
                    PostInterestKind.Publisher => publishers.GetValueOrDefault(
                        profile.ReferenceId,
                        profile.ReferenceId.ToString()
                    ),
                    _ => profile.ReferenceId.ToString(),
                },
                Score = GetDecayedInterestScore(profile, now),
                InteractionCount = profile.InteractionCount,
                LastInteractedAt = profile.LastInteractedAt,
                LastSignalType = profile.LastSignalType,
            })
            .OrderByDescending(x => x.Score)
            .Take(20)
            .ToList();
    }

    private async Task<DiscoverySuggestionContext> BuildDiscoverySuggestionContext(
        DyAccount currentUser,
        List<Guid> userFriends,
        List<Guid> userPublisherIds,
        List<Guid> userRealms,
        List<SnDiscoveryPreference> preferences,
        List<SnPost> candidatePosts,
        List<SnRealm> publicRealms,
        IReadOnlyDictionary<Guid, double> tagInterest,
        IReadOnlyDictionary<Guid, double> categoryInterest,
        IReadOnlyDictionary<Guid, double> publisherInterest,
        Instant now
    )
    {
        var hiddenByKind = preferences
            .GroupBy(x => x.Kind)
            .ToDictionary(x => x.Key, x => x.Select(y => y.ReferenceId).ToHashSet());
        var hiddenPublisherIds = hiddenByKind.GetValueOrDefault(DiscoveryTargetKind.Publisher, []);
        var hiddenRealmIds = hiddenByKind.GetValueOrDefault(DiscoveryTargetKind.Realm, []);
        var hiddenAccountIds = hiddenByKind.GetValueOrDefault(DiscoveryTargetKind.Account, []);

        var subscribedPublisherIds = (await pub.GetSubscribedPublishers(Guid.Parse(currentUser.Id)))
            .Select(x => x.Id)
            .ToHashSet();
        var publicRealmMap = publicRealms.ToDictionary(x => x.Id, x => x);

        var publisherCandidates = await BuildPublisherSuggestions(
            currentUser,
            userPublisherIds,
            subscribedPublisherIds,
            hiddenPublisherIds,
            candidatePosts,
            tagInterest,
            categoryInterest,
            publisherInterest,
            now
        );
        var accountCandidates = await BuildAccountSuggestions(
            currentUser,
            userFriends,
            hiddenAccountIds,
            publisherCandidates
        );
        var realmCandidates = await BuildRealmSuggestions(
            userRealms,
            hiddenRealmIds,
            candidatePosts,
            publicRealmMap,
            tagInterest,
            categoryInterest,
            now
        );
        var suppressed = await BuildSuppressedSuggestions(preferences, publicRealmMap);

        return new DiscoverySuggestionContext
        {
            Publishers = publisherCandidates.Take(3).ToList(),
            Accounts = accountCandidates.Take(3).ToList(),
            Realms = realmCandidates.Take(3).ToList(),
            Suppressed = suppressed,
        };
    }

    private async Task<List<SnDiscoverySuggestion>> BuildPublisherSuggestions(
        DyAccount currentUser,
        List<Guid> userPublisherIds,
        HashSet<Guid> subscribedPublisherIds,
        HashSet<Guid> hiddenPublisherIds,
        List<SnPost> candidatePosts,
        IReadOnlyDictionary<Guid, double> tagInterest,
        IReadOnlyDictionary<Guid, double> categoryInterest,
        IReadOnlyDictionary<Guid, double> publisherInterest,
        Instant now
    )
    {
        var publisherCandidates = candidatePosts
            .Where(p => p.PublisherId.HasValue)
            .GroupBy(p => p.PublisherId!.Value)
            .Select(group =>
            {
                var posts = group.ToList();
                var score = posts
                    .Select(post => CalculateDiscoveryPostScore(post, tagInterest, categoryInterest, now))
                    .OrderByDescending(x => x)
                    .Take(3)
                    .Sum();
                score += publisherInterest.GetValueOrDefault(group.Key, 0d) * 0.4d;
                return new RankedDiscoveryTarget<Guid>(group.Key, score, BuildReasonLabels(posts));
            })
            .Where(x => x.Score > 0.2d)
            .Where(x => !userPublisherIds.Contains(x.ReferenceId))
            .Where(x => !subscribedPublisherIds.Contains(x.ReferenceId))
            .Where(x => !hiddenPublisherIds.Contains(x.ReferenceId))
            .OrderByDescending(x => x.Score)
            .Take(8)
            .ToList();

        if (publisherCandidates.Count == 0)
            return [];

        var publisherIds = publisherCandidates.Select(x => x.ReferenceId).ToList();
        var publishers = await db.Publishers
            .AsNoTracking()
            .Where(x => publisherIds.Contains(x.Id))
            .ToListAsync();
        var publisherMap = publishers.ToDictionary(x => x.Id);

        return publisherCandidates
            .Where(x => publisherMap.ContainsKey(x.ReferenceId))
            .Select(x =>
            {
                var publisher = publisherMap[x.ReferenceId];
                return new SnDiscoverySuggestion
                {
                    Kind = DiscoveryTargetKind.Publisher,
                    ReferenceId = publisher.Id,
                    Label = publisher.Nick,
                    Score = x.Score,
                    Reasons = x.Reasons,
                    Data = new SnPublisherDiscoveryRef
                    {
                        Id = publisher.Id,
                        Name = publisher.Name,
                        Nick = publisher.Nick,
                        Bio = publisher.Bio,
                        Picture = publisher.Picture,
                        Background = publisher.Background,
                    },
                };
            })
            .ToList();
    }

    private async Task<List<SnDiscoverySuggestion>> BuildAccountSuggestions(
        DyAccount currentUser,
        List<Guid> userFriends,
        HashSet<Guid> hiddenAccountIds,
        List<SnDiscoverySuggestion> publisherSuggestions
    )
    {
        var currentUserId = Guid.Parse(currentUser.Id);
        var publisherIds = publisherSuggestions
            .Where(x => x.Kind == DiscoveryTargetKind.Publisher)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();

        if (publisherIds.Count == 0)
            return [];

        var candidatePublishers = await db.Publishers
            .AsNoTracking()
            .Where(x => publisherIds.Contains(x.Id))
            .Where(x => x.AccountId.HasValue && x.Type == PublisherType.Individual)
            .Where(x => x.AccountId != currentUserId)
            .Where(x => !userFriends.Contains(x.AccountId!.Value))
            .Where(x => !hiddenAccountIds.Contains(x.AccountId!.Value))
            .ToListAsync();

        if (candidatePublishers.Count == 0)
            return [];

        var accountMap = (await remoteAccounts.GetAccountBatch(
            candidatePublishers.Select(x => x.AccountId!.Value).Distinct().ToList()
        )).ToDictionary(x => Guid.Parse(x.Id), SnAccount.FromProtoValue);

        return candidatePublishers
            .Where(x => x.AccountId.HasValue && accountMap.ContainsKey(x.AccountId.Value))
            .Select(x =>
            {
                var sourceSuggestion = publisherSuggestions.First(y => y.ReferenceId == x.Id);
                var account = PrepareDiscoveryAccount(accountMap[x.AccountId!.Value]);
                return new SnDiscoverySuggestion
                {
                    Kind = DiscoveryTargetKind.Account,
                    ReferenceId = x.AccountId!.Value,
                    Label = account.Nick,
                    Score = sourceSuggestion.Score,
                    Reasons = sourceSuggestion.Reasons,
                    Data = account,
                };
            })
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private Task<List<SnDiscoverySuggestion>> BuildRealmSuggestions(
        List<Guid> userRealms,
        HashSet<Guid> hiddenRealmIds,
        List<SnPost> candidatePosts,
        IReadOnlyDictionary<Guid, SnRealm> publicRealmMap,
        IReadOnlyDictionary<Guid, double> tagInterest,
        IReadOnlyDictionary<Guid, double> categoryInterest,
        Instant now
    )
    {
        var suggestions = candidatePosts
            .Where(p => p.RealmId.HasValue && publicRealmMap.ContainsKey(p.RealmId.Value))
            .GroupBy(p => p.RealmId!.Value)
            .Select(group =>
            {
                var posts = group.ToList();
                var score = posts
                    .Select(post => CalculateDiscoveryPostScore(post, tagInterest, categoryInterest, now))
                    .OrderByDescending(x => x)
                    .Take(3)
                    .Sum();
                score += publicRealmMap[group.Key].BoostLevel * RealmBoostLevelRankBonus;
                return new RankedDiscoveryTarget<Guid>(group.Key, score, BuildReasonLabels(posts));
            })
            .Where(x => x.Score > 0.2d)
            .Where(x => !userRealms.Contains(x.ReferenceId))
            .Where(x => !hiddenRealmIds.Contains(x.ReferenceId))
            .OrderByDescending(x => x.Score)
            .Take(8)
            .Select(x => new SnDiscoverySuggestion
            {
                Kind = DiscoveryTargetKind.Realm,
                ReferenceId = x.ReferenceId,
                Label = publicRealmMap[x.ReferenceId].Name,
                Score = x.Score,
                Reasons = x.Reasons,
                Data = PrepareDiscoveryRealm(publicRealmMap[x.ReferenceId]),
            })
            .ToList();

        return Task.FromResult(suggestions);
    }

    private async Task<List<SnDiscoverySuggestion>> BuildSuppressedSuggestions(
        List<SnDiscoveryPreference> preferences,
        IReadOnlyDictionary<Guid, SnRealm> publicRealmMap
    )
    {
        var publisherIds = preferences
            .Where(x => x.Kind == DiscoveryTargetKind.Publisher)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();
        var accountIds = preferences
            .Where(x => x.Kind == DiscoveryTargetKind.Account)
            .Select(x => x.ReferenceId)
            .Distinct()
            .ToList();

        var publisherMap = await db.Publishers
            .AsNoTracking()
            .Where(x => publisherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x);
        var accountMap = accountIds.Count == 0
            ? new Dictionary<Guid, SnAccount>()
            : (await remoteAccounts.GetAccountBatch(accountIds))
                .ToDictionary(x => Guid.Parse(x.Id), SnAccount.FromProtoValue);

        return preferences
            .Select(preference => preference.Kind switch
            {
                DiscoveryTargetKind.Publisher when publisherMap.TryGetValue(preference.ReferenceId, out var publisher)
                    => new SnDiscoverySuggestion
                    {
                        Kind = preference.Kind,
                        ReferenceId = preference.ReferenceId,
                        Label = publisher.Nick,
                        Reasons = preference.Reason is null ? [] : [preference.Reason],
                        Data = new SnPublisherDiscoveryRef
                        {
                            Id = publisher.Id,
                            Name = publisher.Name,
                            Nick = publisher.Nick,
                            Bio = publisher.Bio,
                            Picture = publisher.Picture,
                            Background = publisher.Background,
                        },
                    },
                DiscoveryTargetKind.Account when accountMap.TryGetValue(preference.ReferenceId, out var account)
                    => new SnDiscoverySuggestion
                    {
                        Kind = preference.Kind,
                        ReferenceId = preference.ReferenceId,
                        Label = account.Nick,
                        Reasons = preference.Reason is null ? [] : [preference.Reason],
                        Data = PrepareDiscoveryAccount(account),
                    },
                DiscoveryTargetKind.Realm when publicRealmMap.TryGetValue(preference.ReferenceId, out var realm)
                    => new SnDiscoverySuggestion
                    {
                        Kind = preference.Kind,
                        ReferenceId = preference.ReferenceId,
                        Label = realm.Name,
                        Reasons = preference.Reason is null ? [] : [preference.Reason],
                        Data = PrepareDiscoveryRealm(realm),
                    },
                _ => null,
            })
            .Where(x => x != null)
            .Cast<SnDiscoverySuggestion>()
            .ToList();
    }

    private static SnAccount PrepareDiscoveryAccount(SnAccount account)
    {
        account.Profile ??= new SnAccountProfile
        {
            AccountId = account.Id,
            Links = [],
        };
        account.Contacts ??= [];
        account.Badges ??= [];
        account.Profile.Links ??= [];
        return account;
    }

    private static SnRealm PrepareDiscoveryRealm(SnRealm realm)
    {
        realm.Slug ??= string.Empty;
        realm.Name ??= string.Empty;
        realm.Description ??= string.Empty;
        return realm;
    }

    private static double CalculateDiscoveryPostScore(
        SnPost post,
        IReadOnlyDictionary<Guid, double> tagInterest,
        IReadOnlyDictionary<Guid, double> categoryInterest,
        Instant now
    )
    {
        var tagScore = post.Tags.Sum(tag => tagInterest.GetValueOrDefault(tag.Id, 0d)) * 0.9d;
        var categoryScore = post.Categories.Sum(category => categoryInterest.GetValueOrDefault(category.Id, 0d))
            * 0.8d;
        var engagementScore = Math.Max(
            0d,
            post.ReactionScore * 0.15d + (double)post.AwardedScore / 50d + post.RepliesCount * 0.08d
        );
        var articleBonus = post.Type == PostType.Article ? 0.35d : 0d;
        var ageDays = Math.Max(0d, (now - GetPostTimelineInstant(post)).TotalDays);
        var freshness = 1d / Math.Pow(ageDays + 1d, 0.35d);
        return (tagScore + categoryScore + engagementScore + articleBonus) * freshness;
    }

    private static List<string> BuildReasonLabels(IEnumerable<SnPost> posts)
    {
        return posts
            .SelectMany(post => post.Tags.Select(tag => tag.Name ?? tag.Slug))
            .Concat(posts.SelectMany(post => post.Categories.Select(category => category.Name ?? category.Slug)))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct()
            .Take(3)
            .ToList();
    }

    private static SnDiscoverySuggestion PickDiscoverySuggestion(
        IReadOnlyList<SnDiscoverySuggestion> suggestions
    )
    {
        var candidateCount = Math.Min(3, suggestions.Count);
        return suggestions[Random.Shared.Next(candidateCount)];
    }

    private static List<SnPost> DiversifyRankedPosts(
        IReadOnlyList<RankedPostCandidate> candidates,
        int take
    )
    {
        var selected = new List<SnPost>();
        var remaining = candidates.ToList();
        var publisherCounts = new Dictionary<Guid, int>();

        while (selected.Count < take && remaining.Count > 0)
        {
            var next = remaining
                .Select(candidate =>
                {
                    var penalty = 0d;
                    if (candidate.Post.PublisherId.HasValue)
                        penalty = publisherCounts.GetValueOrDefault(candidate.Post.PublisherId.Value, 0)
                            * PublisherRepeatPenalty;
                    return new
                    {
                        Candidate = candidate,
                        FinalRank = candidate.Rank - penalty,
                    };
                })
                .OrderByDescending(x => x.FinalRank)
                .First();

            next.Candidate.Post.DebugRank = next.FinalRank;
            selected.Add(next.Candidate.Post);
            remaining.Remove(next.Candidate);

            if (next.Candidate.Post.PublisherId.HasValue)
                publisherCounts[next.Candidate.Post.PublisherId.Value] =
                    publisherCounts.GetValueOrDefault(next.Candidate.Post.PublisherId.Value, 0) + 1;
        }

        return selected;
    }

    private static List<SnPost> SortLatestPosts(
        IEnumerable<SnPost> posts,
        int take
    )
    {
        return posts
            .Where(p =>
                !(p.PublisherId.HasValue && p.Publisher?.IsShadowbanned == true) &&
                !p.IsShadowbanned)
            .OrderByDescending(GetPostTimelineInstant)
            .Take(take)
            .Select(post =>
            {
                post.DebugRank = 0d;
                return post;
            })
            .ToList();
    }

    private static SnTimelinePage BuildTimelinePage(
        List<SnTimelineEvent> activities,
        IReadOnlyList<SnPost> posts,
        SnTimelineMode mode
    )
    {
        return new SnTimelinePage
        {
            Items = activities,
            NextCursor = GetNextCursor(posts),
            Mode = mode.ToString().ToLowerInvariant(),
        };
    }

    private static string? GetNextCursor(IReadOnlyList<SnPost> posts)
    {
        if (posts.Count == 0)
            return null;

        var oldestPostTime = posts.Min(GetPostTimelineInstant);
        return InstantPattern.ExtendedIso.Format(oldestPostTime);
    }

    private static Instant GetPostTimelineInstant(SnPost post)
    {
        return post.PublishedAt ?? post.CreatedAt;
    }

    private sealed class RankedPostCandidate
    {
        public required SnPost Post { get; init; }
        public required double Rank { get; init; }
        public bool ShouldHide { get; init; }
    }

    private sealed record RankedDiscoveryTarget<T>(T ReferenceId, double Score, List<string> Reasons)
        where T : notnull;

    private sealed class DiscoverySuggestionContext
    {
        public List<SnDiscoverySuggestion> Publishers { get; init; } = [];
        public List<SnDiscoverySuggestion> Accounts { get; init; } = [];
        public List<SnDiscoverySuggestion> Realms { get; init; } = [];
        public List<SnDiscoverySuggestion> Suppressed { get; init; } = [];
    }

    private sealed class RecentServedPostEntry
    {
        public Guid PostId { get; init; }
        public Instant ServedAt { get; init; }
    }

    private async Task<List<SnPublisher>> GetPopularPublishers(int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var recent = now.Minus(Duration.FromDays(7));

        var posts = await db
            .Posts.Where(p => p.DraftedAt == null)
            .Where(p => p.PublishedAt > recent)
            .ToListAsync();

        var publisherIds = posts.Select(p => p.PublisherId).Distinct().ToList();
        var publishers = await db.Publishers.Where(p => publisherIds.Contains(p.Id)).ToListAsync();

        return publishers
            .Select(p => new
            {
                Publisher = p,
                Rank = CalculatePopularity(posts.Where(post => post.PublisherId == p.Id).ToList()),
            })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Publisher)
            .Take(take)
            .ToList();
    }

    private async Task<SnTimelineEvent?> GetRealmDiscoveryActivity(int count = 5)
    {
        var realms = await rs.GetPublicRealms("random", count);
        return realms.Count > 0
            ? new TimelineDiscoveryEvent(
                realms.Select(x => new DiscoveryItem("realm", x)).ToList()
            ).ToActivity()
            : null;
    }

    private async Task<SnTimelineEvent?> GetPublisherDiscoveryActivity(int count = 5)
    {
        var popularPublishers = await GetPopularPublishers(count);
        return popularPublishers.Count > 0
            ? new TimelineDiscoveryEvent(
                popularPublishers.Select(x => new DiscoveryItem("publisher", x)).ToList()
            ).ToActivity()
            : null;
    }

    private async Task<SnTimelineEvent?> GetShuffledPostsActivity(int count = 5)
    {
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();

        var postsQuery = db
            .Posts.Include(p => p.Categories)
            .Include(p => p.Tags)
            .Where(p => p.DraftedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => p.RepliedPostId == null)
            .Where(p => p.RealmId == null || publicRealmIds.Contains(p.RealmId.Value))
            .OrderBy(_ => EF.Functions.Random())
            .Take(count);

        var posts = await GetAndProcessPosts(postsQuery);
        await LoadPostsRealmsAsync(posts, rs);

        return posts.Count == 0
            ? null
            : new TimelineDiscoveryEvent(
                posts.Select(x => new DiscoveryItem("post", x)).ToList()
            ).ToActivity();
    }

    private async Task<List<SnPost>> GetAndProcessPosts(
        IQueryable<SnPost> baseQuery,
        DyAccount? currentUser = null
    )
    {
        var posts = await baseQuery.ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);

        foreach (var post in posts)
        {
            post.ReactionsCount = reactionMaps.GetValueOrDefault(
                post.Id,
                new Dictionary<string, int>()
            );
        }

        return posts;
    }

    private async Task<Dictionary<Guid, double>> GetRecentServedPenaltyMap(
        Guid accountId,
        IReadOnlyList<SnPost> posts,
        Instant now
    )
    {
        var recentEntries = await GetRecentServedPostsAsync(accountId);
        if (recentEntries.Count == 0 || posts.Count == 0)
            return [];

        var entriesByPostId = recentEntries
            .GroupBy(x => x.PostId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.ServedAt).ToList());

        return posts.ToDictionary(
            post => post.Id,
            post =>
            {
                if (!entriesByPostId.TryGetValue(post.Id, out var entries))
                    return 0d;

                var penalty = 0d;
                var ageMinutes = Math.Max(0d, (now - entries[0].ServedAt).TotalMinutes);
                if (ageMinutes <= 15d)
                    penalty += 6d;
                else if (ageMinutes <= 120d)
                    penalty += 3d;
                else if (ageMinutes <= 1440d)
                    penalty += 1d;

                penalty += Math.Min(2d, Math.Max(0, entries.Count - 1) * 0.6d);
                return penalty;
            }
        );
    }

    private async Task<List<RecentServedPostEntry>> GetRecentServedPostsAsync(Guid accountId)
    {
        var cacheKey = GetRecentServedPostsCacheKey(accountId);
        var entries = await cache.GetAsync<List<RecentServedPostEntry>>(cacheKey);
        if (entries is null)
            return [];

        var now = SystemClock.Instance.GetCurrentInstant();
        var validEntries = entries
            .Where(x => (now - x.ServedAt).TotalHours <= RecentServedPostsCacheTtl.TotalHours)
            .OrderByDescending(x => x.ServedAt)
            .Take(RecentServedPostLimit)
            .ToList();

        if (validEntries.Count != entries.Count)
            await cache.SetAsync(cacheKey, validEntries, RecentServedPostsCacheTtl);

        return validEntries;
    }

    private async Task RememberServedPostsAsync(IEnumerable<SnPost> posts, Guid accountId)
    {
        var cacheKey = GetRecentServedPostsCacheKey(accountId);
        var existingEntries = await GetRecentServedPostsAsync(accountId);
        var now = SystemClock.Instance.GetCurrentInstant();

        var updatedEntries = posts
            .Select(post => new RecentServedPostEntry
            {
                PostId = post.Id,
                ServedAt = now,
            })
            .Concat(existingEntries)
            .OrderByDescending(x => x.ServedAt)
            .Take(RecentServedPostLimit)
            .ToList();

        await cache.SetAsync(cacheKey, updatedEntries, RecentServedPostsCacheTtl);
    }

    private static string GetRecentServedPostsCacheKey(Guid accountId)
    {
        return $"timeline:recent-served:{accountId}";
    }

    private async Task<List<Guid>> GetCachedFriendIds(Guid accountId, string accountIdString)
    {
        var cacheKey = $"timeline:friends:{accountId}";
        var cached = await cache.GetAsync<List<Guid>>(cacheKey);
        if (cached is not null)
            return cached;

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = accountIdString }
        );
        var friendIds = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        await cache.SetAsync(cacheKey, friendIds, FriendIdsCacheTtl);
        return friendIds;
    }

    private async Task<List<Guid>> GetCachedUserRealms(Guid accountId)
    {
        var cacheKey = $"timeline:realms:{accountId}";
        var cached = await cache.GetAsync<List<Guid>>(cacheKey);
        if (cached is not null)
            return cached;

        var realmIds = await rs.GetUserRealms(accountId);
        await cache.SetAsync(cacheKey, realmIds, UserRealmsCacheTtl);
        return realmIds;
    }

    private IQueryable<SnPost> BuildPostsQuery(
        Instant? cursor,
        List<Guid>? filteredPublishersId = null,
        List<Guid>? userRealms = null
    )
    {
        var query = db
            .Posts.Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .Where(e => e.DraftedAt == null)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .AsNoTracking()
            .AsQueryable();

        if (filteredPublishersId != null && filteredPublishersId.Count != 0)
            query = query.Where(p =>
                p.PublisherId.HasValue && filteredPublishersId.Contains(p.PublisherId.Value)
            );
        if (userRealms == null)
        {
            query = query.Where(p => p.RealmId == null);
        }
        else
            query = query.Where(p => p.RealmId == null || userRealms.Contains(p.RealmId.Value));

        return query;
    }

    private async Task<List<SnPublisher>?> GetFilteredPublishers(
        string? filter,
        DyAccount currentUser,
        List<Guid> userFriends
    )
    {
        return filter?.ToLower() switch
        {
            "subscriptions" => await pub.GetSubscribedPublishers(Guid.Parse(currentUser.Id)),
            "friends" => (await pub.GetUserPublishersBatch(userFriends))
                .SelectMany(x => x.Value)
                .DistinctBy(x => x.Id)
                .ToList(),
            _ => null,
        };
    }

    private static async Task LoadPostsRealmsAsync(List<SnPost> posts, RemoteRealmService rs)
    {
        var postRealmIds = posts
            .Where(p => p.RealmId != null)
            .Select(p => p.RealmId!.Value)
            .Distinct()
            .ToList();
        if (postRealmIds.Count == 0)
            return;

        var realms = await rs.GetRealmBatch(postRealmIds.Select(id => id.ToString()).ToList());
        var realmDict = realms.ToDictionary(r => r.Id, r => r);

        foreach (var post in posts.Where(p => p.RealmId != null))
        {
            if (realmDict.TryGetValue(post.RealmId!.Value, out var realm))
                post.Realm = realm;
        }
    }

    private static double CalculatePopularity(List<SnPost> posts)
    {
        var score = posts.Sum(p => p.Upvotes - p.Downvotes);
        var postCount = posts.Count;
        return score + postCount;
    }
}