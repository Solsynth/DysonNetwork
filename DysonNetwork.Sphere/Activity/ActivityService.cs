using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Discovery;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.WebReader;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(
    AppDatabase db,
    Publisher.PublisherService pub,
    PostService ps,
    RemoteRealmService rs,
    DiscoveryService ds,
    AccountService.AccountServiceClient accounts
)
{
    private static double CalculateHotRank(SnPost post, Instant now)
    {
        var performanceScore = post.Upvotes - post.Downvotes + post.RepliesCount + (int)post.AwardedScore / 10;
        var postTime = post.PublishedAt ?? post.CreatedAt;
        var timeScore = (now - postTime).TotalMinutes;
        // Add 1 to score to prevent negative results for posts with more downvotes than upvotes
        // Time dominates ranking, performance adjusts within similar timeframes.
        var performanceWeight = performanceScore + 5;
        // Normalize time influence since average post interval ~60 minutes
        var normalizedTime = timeScore / 60.0; 
        return performanceWeight / Math.Pow(normalizedTime + 1.0, 1.2);
    }

    public async Task<List<SnActivity>> GetActivitiesForAnyone(
        int take,
        Instant? cursor,
        HashSet<string>? debugInclude = null)
    {
        var activities = new List<SnActivity>();
        debugInclude ??= new HashSet<string>();

        // Get and process posts
        var postsQuery = db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.Realm)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(null, [], [], isListing: true)
            .Take(take * 5);

        var posts = await GetAndProcessPosts(postsQuery);
        posts = RankPosts(posts, take);

        var interleaved = new List<SnActivity>();
        var random = new Random();
        foreach (var post in posts)
        {
            // Randomly insert a discovery activity before some posts
            if (random.NextDouble() < 0.15)
            {
                var discovery = await MaybeGetDiscoveryActivity(debugInclude, cursor: cursor);
                if (discovery != null)
                    interleaved.Add(discovery);
            }

            interleaved.Add(post.ToActivity());
        }

        activities.AddRange(interleaved);

        if (activities.Count == 0)
            activities.Add(SnActivity.Empty());

        return activities;
    }

    public async Task<List<SnActivity>> GetActivities(
        int take,
        Instant? cursor,
        Account currentUser,
        string? filter = null,
        HashSet<string>? debugInclude = null)
    {
        var activities = new List<SnActivity>();
        debugInclude ??= new HashSet<string>();

        // Get user's friends and publishers
        var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
        {
            AccountId = currentUser.Id
        });
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        // Get publishers based on filter
        var filteredPublishers = await GetFilteredPublishers(filter, currentUser, userFriends);
        var filteredPublishersId = filteredPublishers?.Select(e => e.Id).ToList();

        var userRealms = await rs.GetUserRealms(Guid.Parse(currentUser.Id));

        // Build and execute the posts query
        var postsQuery = BuildPostsQuery(cursor, filteredPublishersId, userRealms);

        // Apply visibility filtering and execute
        postsQuery = postsQuery
            .FilterWithVisibility(
                currentUser,
                userFriends,
                filter is null ? userPublishers : [],
                isListing: true)
            .Take(take * 5);

        // Get, process and rank posts
        var posts = await GetAndProcessPosts(
            postsQuery,
            currentUser,
            userFriends,
            userPublishers,
            trackViews: true);

        posts = RankPosts(posts, take);

        var interleaved = new List<SnActivity>();
        var random = new Random();
        foreach (var post in posts)
        {
            if (random.NextDouble() < 0.15)
            {
                var discovery = await MaybeGetDiscoveryActivity(debugInclude, cursor: cursor);
                if (discovery != null)
                    interleaved.Add(discovery);
            }

            interleaved.Add(post.ToActivity());
        }

        activities.AddRange(interleaved);

        if (activities.Count == 0)
            activities.Add(SnActivity.Empty());

        return activities;
    }

    private async Task<SnActivity?> MaybeGetDiscoveryActivity(HashSet<string> debugInclude, Instant? cursor)
    {
        if (cursor != null) return null;
        var options = new List<Func<Task<SnActivity?>>>();
        if (debugInclude.Contains("realms") || Random.Shared.NextDouble() < 0.2)
            options.Add(() => GetRealmDiscoveryActivity());
        if (debugInclude.Contains("publishers") || Random.Shared.NextDouble() < 0.2)
            options.Add(() => GetPublisherDiscoveryActivity());
        if (debugInclude.Contains("articles") || Random.Shared.NextDouble() < 0.2)
            options.Add(() => GetArticleDiscoveryActivity());
        if (debugInclude.Contains("shuffledPosts") || Random.Shared.NextDouble() < 0.2)
            options.Add(() => GetShuffledPostsActivity());
        if (options.Count == 0) return null;
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

    private async Task<List<Shared.Models.SnPublisher>> GetPopularPublishers(int take)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var recent = now.Minus(Duration.FromDays(7));

        var posts = await db.Posts
            .Where(p => p.PublishedAt > recent)
            .ToListAsync();

        var publisherIds = posts.Select(p => p.PublisherId).Distinct().ToList();
        var publishers = await db.Publishers.Where(p => publisherIds.Contains(p.Id)).ToListAsync();

        return publishers
            .Select(p => new
            {
                Publisher = p,
                Rank = CalculatePopularity(posts.Where(post => post.PublisherId == p.Id).ToList())
            })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Publisher)
            .Take(take)
            .ToList();
    }

    private async Task<SnActivity?> GetRealmDiscoveryActivity(int count = 5)
    {
        var realms = await ds.GetCommunityRealmAsync(null, count, 0, true);
        return realms.Count > 0
            ? new DiscoveryActivity(realms.Select(x => new DiscoveryItem("realm", x)).ToList()).ToActivity()
            : null;
    }

    private async Task<SnActivity?> GetPublisherDiscoveryActivity(int count = 5)
    {
        var popularPublishers = await GetPopularPublishers(count);
        return popularPublishers.Count > 0
            ? new DiscoveryActivity(popularPublishers.Select(x => new DiscoveryItem("publisher", x)).ToList())
                .ToActivity()
            : null;
    }

    private async Task<SnActivity?> GetShuffledPostsActivity(int count = 5)
    {
        var postsQuery = db.Posts
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.Realm)
            .Where(p => p.RepliedPostId == null)
            .OrderBy(_ => EF.Functions.Random())
            .Take(count);

        var posts = await GetAndProcessPosts(postsQuery, trackViews: false);

        return posts.Count == 0
            ? null
            : new DiscoveryActivity(posts.Select(x => new DiscoveryItem("post", x)).ToList()).ToActivity();
    }

    private async Task<SnActivity?> GetArticleDiscoveryActivity(int count = 5, int feedSampleSize = 10)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var today = now.InZone(DateTimeZone.Utc).Date;
        var todayBegin = today.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var todayEnd = today.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var recentFeedIds = await db.WebArticles
            .Where(a => a.CreatedAt >= todayBegin && a.CreatedAt < todayEnd)
            .GroupBy(a => a.FeedId)
            .OrderByDescending(g => g.Max(a => a.PublishedAt))
            .Take(feedSampleSize)
            .Select(g => g.Key)
            .ToListAsync();

        var recentArticles = new List<WebArticle>();
        var random = new Random();

        foreach (var feedId in recentFeedIds.OrderBy(_ => random.Next()))
        {
            var article = await db.WebArticles
                .Include(a => a.Feed)
                .Where(a => a.FeedId == feedId)
                .OrderBy(_ => EF.Functions.Random())
                .FirstOrDefaultAsync();

            if (article == null) continue;
            recentArticles.Add(article);
            if (recentArticles.Count >= count) break;
        }

        return recentArticles.Count > 0
            ? new DiscoveryActivity(recentArticles.Select(x => new DiscoveryItem("article", x)).ToList()).ToActivity()
            : null;
    }

    private async Task<List<SnPost>> GetAndProcessPosts(
        IQueryable<SnPost> baseQuery,
        Account? currentUser = null,
        List<Guid>? userFriends = null,
        List<Shared.Models.SnPublisher>? userPublishers = null,
        bool trackViews = true)
    {
        var posts = await baseQuery.ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);

        foreach (var post in posts)
        {
            post.ReactionsCount = reactionMaps.GetValueOrDefault(post.Id, new Dictionary<string, int>());

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
        var query = db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.Realm)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .AsQueryable();

        if (filteredPublishersId != null && filteredPublishersId.Count != 0)
            query = query.Where(p => filteredPublishersId.Contains(p.PublisherId));
        if (userRealms == null)
            query = query.Where(p => p.Realm == null || p.Realm.IsPublic);
        else
            query = query.Where(p =>
                p.Realm == null || p.Realm.IsPublic || p.RealmId == null || userRealms.Contains(p.RealmId.Value));

        return query;
    }

    private async Task<List<Shared.Models.SnPublisher>?> GetFilteredPublishers(
        string? filter,
        Account currentUser,
        List<Guid> userFriends)
    {
        return filter?.ToLower() switch
        {
            "subscriptions" => await pub.GetSubscribedPublishers(Guid.Parse(currentUser.Id)),
            "friends" => (await pub.GetUserPublishersBatch(userFriends))
                .SelectMany(x => x.Value)
                .DistinctBy(x => x.Id)
                .ToList(),
            _ => null
        };
    }

    private static double CalculatePopularity(List<SnPost> posts)
    {
        var score = posts.Sum(p => p.Upvotes - p.Downvotes);
        var postCount = posts.Count;
        return score + postCount;
    }
}
