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
    AccountService.AccountServiceClient accounts,
    RemoteWebArticleService webArticles
)
{
    private static double CalculateHotRank(SnPost post, Instant now)
    {
        var performanceScore =
            post.Upvotes - post.Downvotes + post.RepliesCount + (int)post.AwardedScore / 10;
        var postTime = post.PublishedAt ?? post.CreatedAt;
        var timeScore = (now - postTime).TotalMinutes;
        // Add 1 to score to prevent negative results for posts with more downvotes than upvotes
        // Time dominates ranking, performance adjusts within similar timeframes.
        var performanceWeight = performanceScore + 5;
        // Normalize time influence since average post interval ~60 minutes
        var normalizedTime = timeScore / 60.0;
        return performanceWeight / Math.Pow(normalizedTime + 1.0, 1.2);
    }

    public async Task<List<SnTimelineEvent>> ListEventsForAnyone(int take, Instant? cursor, bool showFediverse = false)
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
        posts = RankPosts(posts, take);

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
        Account currentUser,
        string? filter = null,
        bool showFediverse = false
    )
    {
        var activities = new List<SnTimelineEvent>();

        // Get user's friends and publishers
        var friendsResponse = await accounts.ListFriendsAsync(
            new ListRelationshipSimpleRequest { RelatedId = currentUser.Id }
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

        posts = RankPosts(posts, take);

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
            options.Add(() => GetArticleDiscoveryActivity());
        if (Random.Shared.NextDouble() < 0.5)
            options.Add(() => GetShuffledPostsActivity());
        if (options.Count == 0)
            return null;
        var random = new Random();
        var pick = options[random.Next(options.Count)];
        return await pick();
    }

    private static List<SnPost> RankPosts(List<SnPost> posts, int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return posts
            .Select(p => new { Post = p, Rank = CalculateHotRank(p, now) })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Post)
            .Take(take)
            .ToList();
        // return posts.Take(take).ToList();
    }

    private async Task<List<SnPublisher>> GetPopularPublishers(int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var recent = now.Minus(Duration.FromDays(7));

        var posts = await db.Posts.Where(p => p.PublishedAt > recent).ToListAsync();

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

        var postsQuery = db.Posts
            .Include(p => p.Categories)
            .Include(p => p.Tags)
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

    private async Task<SnTimelineEvent?> GetArticleDiscoveryActivity(int count = 5)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var recentArticles = await webArticles.GetRecentArticles(count);

        return recentArticles.Count > 0
            ? new TimelineDiscoveryEvent(
                recentArticles.Select(x => new DiscoveryItem("article", x)).ToList()
            ).ToActivity()
            : null;
    }

    private async Task<List<SnPost>> GetAndProcessPosts(
        IQueryable<SnPost> baseQuery,
        Account? currentUser = null,
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
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .AsQueryable();

        if (filteredPublishersId != null && filteredPublishersId.Count != 0)
            query = query.Where(p => p.PublisherId.HasValue && filteredPublishersId.Contains(p.PublisherId.Value));
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
        Account currentUser,
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