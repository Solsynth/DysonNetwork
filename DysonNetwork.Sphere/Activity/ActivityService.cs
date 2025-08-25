using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Discovery;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.WebReader;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(
    AppDatabase db,
    Publisher.PublisherService pub,
    PostService ps,
    RealmService rs,
    DiscoveryService ds,
    AccountService.AccountServiceClient accounts
)
{
    private static double CalculateHotRank(Post.Post post, Instant now)
    {
        var score = post.Upvotes - post.Downvotes + post.RepliesCount;
        var postTime = post.PublishedAt ?? post.CreatedAt;
        var hours = (now - postTime).TotalHours;
        // Add 1 to score to prevent negative results for posts with more downvotes than upvotes
        return (score + 1) / Math.Pow(hours + 2, 1.8);
    }

    public async Task<List<Activity>> GetActivitiesForAnyone(
        int take,
        Instant? cursor,
        HashSet<string>? debugInclude = null)
    {
        var activities = new List<Activity>();
        debugInclude ??= new HashSet<string>();

        // Add realm discovery if needed
        if (cursor == null && (debugInclude.Contains("realms") || Random.Shared.NextDouble() < 0.2))
        {
            var realmActivity = await GetRealmDiscoveryActivity();
            if (realmActivity != null)
                activities.Add(realmActivity);
        }

        // Add article discovery if needed
        if (debugInclude.Contains("articles") || Random.Shared.NextDouble() < 0.2)
        {
            var articleActivity = await GetArticleDiscoveryActivity();
            if (articleActivity != null)
                activities.Add(articleActivity);
        }

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

        // Add posts to activities
        activities.AddRange(posts.Select(post => post.ToActivity()));

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }

    public async Task<List<Activity>> GetActivities(
        int take,
        Instant? cursor,
        Account currentUser,
        string? filter = null,
        HashSet<string>? debugInclude = null)
    {
        var activities = new List<Activity>();
        debugInclude ??= [];

        // Get user's friends and publishers
        var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
        {
            AccountId = currentUser.Id
        });
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        // Add discovery activities if no specific filter is applied
        if (string.IsNullOrEmpty(filter))
        {
            // Add realm discovery if needed
            if (cursor == null && (debugInclude.Contains("realms") || Random.Shared.NextDouble() < 0.2))
            {
                var realmActivity = await GetRealmDiscoveryActivity();
                if (realmActivity != null)
                    activities.Add(realmActivity);
            }

            // Add publisher discovery if needed
            if (cursor == null && (debugInclude.Contains("publishers") || Random.Shared.NextDouble() < 0.2))
            {
                var publisherActivity = await GetPublisherDiscoveryActivity();
                if (publisherActivity != null)
                    activities.Add(publisherActivity);
            }

            // Add article discovery if needed
            if (debugInclude.Contains("articles") || Random.Shared.NextDouble() < 0.2)
            {
                var articleActivity = await GetArticleDiscoveryActivity();
                if (articleActivity != null)
                    activities.Add(articleActivity);
            }
        }

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

        // Add posts to activities
        activities.AddRange(posts.Select(post => post.ToActivity()));

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }

    private static List<Post.Post> RankPosts(List<Post.Post> posts, int take)
    {
        // TODO: This feature is disabled for now
        // Uncomment and implement when ready
        /*
        var now = SystemClock.Instance.GetCurrentInstant();
        return posts
            .Select(p => new { Post = p, Rank = CalculateHotRank(p, now) })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Post)
            .Take(take)
            .ToList();
        */
        return posts.Take(take).ToList();
    }

    private async Task<List<Publisher.Publisher>> GetPopularPublishers(int take)
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

    private async Task<Activity?> GetRealmDiscoveryActivity(int count = 5)
    {
        var realms = await ds.GetCommunityRealmAsync(null, count, 0, true);
        return realms.Count > 0
            ? new DiscoveryActivity(realms.Select(x => new DiscoveryItem("realm", x)).ToList()).ToActivity()
            : null;
    }

    private async Task<Activity?> GetPublisherDiscoveryActivity(int count = 5)
    {
        var popularPublishers = await GetPopularPublishers(count);
        return popularPublishers.Count > 0
            ? new DiscoveryActivity(popularPublishers.Select(x => new DiscoveryItem("publisher", x)).ToList())
                .ToActivity()
            : null;
    }

    private async Task<Activity?> GetArticleDiscoveryActivity(int count = 5, int feedSampleSize = 10)
    {
        var recentFeedIds = await db.WebArticles
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

    private async Task<List<Post.Post>> GetAndProcessPosts(
        IQueryable<Post.Post> baseQuery,
        Account? currentUser = null,
        List<Guid>? userFriends = null,
        List<Publisher.Publisher>? userPublishers = null,
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

    private IQueryable<Post.Post> BuildPostsQuery(
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

        if (filteredPublishersId != null && filteredPublishersId.Any())
            query = query.Where(p => filteredPublishersId.Contains(p.PublisherId));
        if (userRealms == null)
            query = query.Where(p => p.Realm == null || p.Realm.IsPublic);
        else
            query = query.Where(p =>
                p.Realm == null || p.Realm.IsPublic || p.RealmId == null || userRealms.Contains(p.RealmId.Value));

        return query;
    }

    private async Task<List<Publisher.Publisher>?> GetFilteredPublishers(
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

    private static double CalculatePopularity(List<Post.Post> posts)
    {
        var score = posts.Sum(p => p.Upvotes - p.Downvotes);
        var postCount = posts.Count;
        return score + postCount;
    }
}