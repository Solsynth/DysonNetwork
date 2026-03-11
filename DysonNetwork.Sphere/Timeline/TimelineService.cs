using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PostVisibility = DysonNetwork.Shared.Models.PostVisibility;

namespace DysonNetwork.Sphere.Timeline;

public class TimelineService(
    AppDatabase db,
    Publisher.PublisherService pub,
    Post.PostService ps,
    RemoteRealmService rs,
    DyProfileService.DyProfileServiceClient accounts,
    RemoteAccountService remoteAccounts
)
{
    private const double ArticleTypeBoost = 1.5d;
    private const double PublisherRepeatPenalty = 1.35d;

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

    public async Task<List<SnTimelineEvent>> ListEventsForAnyone(
        int take,
        Instant? cursor,
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
        posts = await RankPosts(posts, take);

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

        return activities;
    }

    public async Task<List<SnTimelineEvent>> ListEvents(
        int take,
        Instant? cursor,
        DyAccount currentUser,
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
        var posts = await GetAndProcessPosts(postsQuery, currentUser, trackViews: true);

        await LoadPostsRealmsAsync(posts, rs);

        posts = await RankPosts(posts, take, currentUser);

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

        return activities;
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

    private async Task<List<SnPost>> RankPosts(
        List<SnPost> posts,
        int take,
        DyAccount? currentUser = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var personalizationBonus = currentUser is null
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

    private sealed class RankedPostCandidate
    {
        public required SnPost Post { get; init; }
        public required double Rank { get; init; }
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
