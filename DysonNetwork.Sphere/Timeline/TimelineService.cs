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
    DysonNetwork.Shared.Cache.ICacheService cache
)
{
    private const double ArticleTypeBoost = 1.5d;
    private const double PublisherRepeatPenalty = 1.35d;
    private const int DiscoveryCandidatePostTake = 96;
    private static readonly TimeSpan DiscoveryProfileCacheTtl = TimeSpan.FromMinutes(3);
    private static readonly Duration DiscoveryLookback = Duration.FromDays(45);

    private static double CalculateBaseRank(SnPost post, Instant now)
    {
        var performanceScore =
            post.ReactionScore * 1.4 + post.ThreadRepliesCount * 0.8 + (double)post.AwardedScore / 10d;
        if (post.Type == PostType.Article)
            performanceScore += ArticleTypeBoost;

        var postTime = post.PublishedAt ?? post.CreatedAt;
        var timeScore = (now - postTime).TotalMinutes;
        var performanceWeight = performanceScore + 5;
        var normalizedTime = timeScore / 60.0;
        return performanceWeight / Math.Pow(normalizedTime + 1.0, 1.2);
    }

    public async Task<SnTimelinePage> ListEventsForAnyone(
        int take,
        Instant? cursor,
        SnTimelineMode mode,
        bool showFediverse = false
    )
    {
        var activities = new List<SnTimelineEvent>();

        // Get and process posts
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();

        var postsQuery = BuildPostsQuery(cursor, null, publicRealmIds)
            .FilterWithVisibility(null, [], [], isListing: true)
            .Take(take * 5);
        if (!showFediverse)
            postsQuery = postsQuery.Where(p => p.FediverseUri == null);

        var posts = await GetAndProcessPosts(postsQuery);
        await LoadPostsRealmsAsync(posts, rs);
        posts = await RankPosts(posts, take, null, mode);

        var interleaved = new List<SnTimelineEvent>();
        var random = new Random();
        foreach (var post in posts)
        {
            // Randomly insert a discovery activity before some posts
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

    public async Task<SnTimelinePage> ListEvents(
        int take,
        Instant? cursor,
        DyAccount currentUser,
        SnTimelineMode mode,
        string? filter = null,
        bool showFediverse = false
    )
    {
        var activities = new List<SnTimelineEvent>();

        // Get user's friends and publishers
        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));
        var userPublisherIds = userPublishers.Select(x => x.Id).ToList();

        // Get publishers based on filter
        var filteredPublishers = await GetFilteredPublishers(filter, currentUser, userFriends);
        var filteredPublishersId = filteredPublishers?.Select(e => e.Id).ToList();

        var userRealms = await rs.GetUserRealms(Guid.Parse(currentUser.Id));

        // Build and execute the post query
        var postsQuery = BuildPostsQuery(cursor, filteredPublishersId, userRealms);
        if (!showFediverse)
            postsQuery = postsQuery.Where(p => p.FediverseUri == null);

        // Apply visibility filtering and execute
        postsQuery = postsQuery
            .FilterWithVisibility(
                currentUser,
                userFriends,
                filter is null ? userPublishers : [],
                isListing: true
            )
            .Take(take * 5);

        // Get, process and rank posts
        var posts = await GetAndProcessPosts(postsQuery, currentUser, trackViews: false);

        await LoadPostsRealmsAsync(posts, rs);

        posts = await RankPosts(posts, take, currentUser, mode);
        await TrackPostViewsAsync(posts, currentUser);

        var interleaved = new List<SnTimelineEvent>();
        var random = new Random();
        SnTimelineEvent? personalizedDiscovery = null;
        foreach (var post in posts)
        {
            if (random.NextDouble() < 0.15)
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
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "publisher",
                "Suggested publisher",
                [profile.SuggestedPublishers[0]]
            ).ToActivity());
        }

        if (profile.SuggestedAccounts.Count > 0)
        {
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "account",
                "People you may know",
                [profile.SuggestedAccounts[0]]
            ).ToActivity());
        }

        if (profile.SuggestedRealms.Count > 0)
        {
            options.Add(() => new PersonalizedTimelineDiscoveryEvent(
                "realm",
                "Suggested realm",
                [profile.SuggestedRealms[0]]
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
        SnTimelineMode mode = SnTimelineMode.Personalized
    )
    {
        if (mode == SnTimelineMode.Latest)
            return SortLatestPosts(posts, take);

        var now = SystemClock.Instance.GetCurrentInstant();
        var personalizationBonus = mode != SnTimelineMode.Personalized || currentUser is null
            ? new Dictionary<Guid, double>()
            : await GetPersonalizationBonusMap(posts, Guid.Parse(currentUser.Id), now);
        var publisherLevelBonus = await GetPublisherLevelBonusMap(posts);
        var rankedCandidates = posts
            .Select(p => new RankedPostCandidate
            {
                Post = p,
                Rank = CalculateBaseRank(p, now)
                    + personalizationBonus.GetValueOrDefault(p.Id, 0d)
                    + publisherLevelBonus.GetValueOrDefault(p.Id, 0d),
            })
            .OrderByDescending(x => x.Rank)
            .ToList();

        return DiversifyRankedPosts(rankedCandidates, take);
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
                    bonus += Math.Min(2d, publisherInterest.GetValueOrDefault(post.PublisherId.Value, 0d) * 0.35d);
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

    private async Task<Dictionary<Guid, double>> GetPublisherLevelBonusMap(List<SnPost> posts)
    {
        var publisherAccounts = posts
            .Where(p => p.Publisher?.AccountId.HasValue == true)
            .Select(p => p.Publisher!.AccountId!.Value)
            .Distinct()
            .ToList();

        if (publisherAccounts.Count == 0)
            return [];

        var accountsBatch = await remoteAccounts.GetAccountBatch(publisherAccounts);
        var socialLevelByAccountId = accountsBatch
            .Where(a => Guid.TryParse(a.Id, out _) && a.Profile is not null)
            .ToDictionary(
                a => Guid.Parse(a.Id),
                a => a.Profile?.SocialCreditsLevel ?? 0
            );

        return posts.ToDictionary(
            p => p.Id,
            p =>
            {
                var accountId = p.Publisher?.AccountId;
                if (!accountId.HasValue)
                    return 0d;

                var socialLevel = socialLevelByAccountId.GetValueOrDefault(accountId.Value, 0);
                return Math.Min(3d, socialLevel * 0.05d);
            }
        );
    }

    public async Task<SnDiscoveryProfile> GetDiscoveryProfile(DyAccount currentUser)
    {
        var accountId = Guid.Parse(currentUser.Id);
        var cacheKey = $"timeline:discovery-profile:{accountId}";
        var cachedProfile = await cache.GetAsync<SnDiscoveryProfile>(cacheKey);
        if (cachedProfile is not null)
            return cachedProfile;

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(accountId);
        var userRealms = await rs.GetUserRealms(accountId);

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
                return new RankedDiscoveryTarget<Guid>(group.Key, score, posts);
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
            .Where(x => publisherIds.Contains(x.Id))
            .ToListAsync();
        publishers = await pub.LoadIndividualPublisherAccounts(publishers);
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
                    Reasons = BuildReasonLabels(x.Posts),
                    Data = publisher,
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
        var candidatePublishers = publisherSuggestions
            .Select(x => x.Data as SnPublisher)
            .Where(x => x is { AccountId: not null, Type: PublisherType.Individual })
            .Select(x => x!)
            .Where(x => x.AccountId != currentUserId)
            .Where(x => !userFriends.Contains(x.AccountId!.Value))
            .Where(x => !hiddenAccountIds.Contains(x.AccountId!.Value))
            .ToList();
        if (candidatePublishers.Count == 0)
            return [];

        var accountMap = (await remoteAccounts.GetAccountBatch(
            candidatePublishers.Select(x => x.AccountId!.Value).Distinct().ToList()
        )).ToDictionary(x => Guid.Parse(x.Id), SnAccount.FromProtoValue);

        return candidatePublishers
            .Where(x => x.AccountId.HasValue && accountMap.ContainsKey(x.AccountId.Value))
            .Select(x =>
            {
                var account = accountMap[x.AccountId!.Value];
                var sourceSuggestion = publisherSuggestions.First(y => y.ReferenceId == x.Id);
                return new SnDiscoverySuggestion
                {
                    Kind = DiscoveryTargetKind.Account,
                    ReferenceId = account.Id,
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
                return new RankedDiscoveryTarget<Guid>(group.Key, score, posts);
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
                Reasons = BuildReasonLabels(x.Posts),
                Data = publicRealmMap[x.ReferenceId],
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
                        Data = publisher,
                    },
                DiscoveryTargetKind.Account when accountMap.TryGetValue(preference.ReferenceId, out var account)
                    => new SnDiscoverySuggestion
                    {
                        Kind = preference.Kind,
                        ReferenceId = preference.ReferenceId,
                        Label = account.Nick,
                        Reasons = preference.Reason is null ? [] : [preference.Reason],
                        Data = account,
                    },
                DiscoveryTargetKind.Realm when publicRealmMap.TryGetValue(preference.ReferenceId, out var realm)
                    => new SnDiscoverySuggestion
                    {
                        Kind = preference.Kind,
                        ReferenceId = preference.ReferenceId,
                        Label = realm.Name,
                        Reasons = preference.Reason is null ? [] : [preference.Reason],
                        Data = realm,
                    },
                _ => null,
            })
            .Where(x => x != null)
            .Cast<SnDiscoverySuggestion>()
            .ToList();
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
    }

    private sealed record RankedDiscoveryTarget<T>(T ReferenceId, double Score, List<SnPost> Posts)
        where T : notnull;

    private sealed class DiscoverySuggestionContext
    {
        public List<SnDiscoverySuggestion> Publishers { get; init; } = [];
        public List<SnDiscoverySuggestion> Accounts { get; init; } = [];
        public List<SnDiscoverySuggestion> Realms { get; init; } = [];
        public List<SnDiscoverySuggestion> Suppressed { get; init; } = [];
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

        var posts = await GetAndProcessPosts(postsQuery, trackViews: false);
        await LoadPostsRealmsAsync(posts, rs);

        return posts.Count == 0
            ? null
            : new TimelineDiscoveryEvent(
                posts.Select(x => new DiscoveryItem("post", x)).ToList()
            ).ToActivity();
    }

    private async Task<List<SnPost>> GetAndProcessPosts(
        IQueryable<SnPost> baseQuery,
        DyAccount? currentUser = null,
        bool trackViews = true
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

            if (trackViews && currentUser != null)
            {
                await ps.IncreaseViewCount(post.Id, currentUser.Id.ToString());
            }
        }

        return posts;
    }

    private async Task TrackPostViewsAsync(IEnumerable<SnPost> posts, DyAccount currentUser)
    {
        foreach (var post in posts)
            await ps.IncreaseViewCount(post.Id, currentUser.Id.ToString());
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
            .AsQueryable();

        if (filteredPublishersId != null && filteredPublishersId.Count != 0)
            query = query.Where(p =>
                p.PublisherId.HasValue && filteredPublishersId.Contains(p.PublisherId.Value)
            );
        if (userRealms == null)
        {
            // For anonymous users, only show public realm posts or posts without realm
            // Get public realm ids in the caller and pass them
            query = query.Where(p => p.RealmId == null); // Modify in caller
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
