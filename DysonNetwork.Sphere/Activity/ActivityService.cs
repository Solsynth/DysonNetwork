using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Connection.WebReader;
using DysonNetwork.Sphere.Discovery;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(
    AppDatabase db,
    PublisherService pub,
    RelationshipService rels,
    PostService ps,
    DiscoveryService ds)
{
    private static double CalculateHotRank(Post.Post post, Instant now)
    {
        var score = post.Upvotes - post.Downvotes;
        var postTime = post.PublishedAt ?? post.CreatedAt;
        var hours = (now - postTime).TotalHours;
        // Add 1 to score to prevent negative results for posts with more downvotes than upvotes
        return (score + 1) / Math.Pow(hours + 2, 1.8);
    }

    public async Task<List<Activity>> GetActivitiesForAnyone(int take, Instant? cursor, HashSet<string>? debugInclude = null)
    {
        var activities = new List<Activity>();
        debugInclude ??= new HashSet<string>();

        if (cursor == null && (debugInclude.Contains("realms") || Random.Shared.NextDouble() < 0.2))
        {
            var realms = await ds.GetPublicRealmsAsync(null, null, 5, 0, true);
            if (realms.Count > 0)
            {
                activities.Add(new DiscoveryActivity(
                    realms.Select(x => new DiscoveryItem("realm", x)).ToList()
                ).ToActivity());
            }
        }

        if (debugInclude.Contains("articles") || Random.Shared.NextDouble() < 0.2)
        {
            var recentFeedIds = await db.WebArticles
                .GroupBy(a => a.FeedId)
                .OrderByDescending(g => g.Max(a => a.PublishedAt))
                .Take(10) // Get recent 10 distinct feeds
                .Select(g => g.Key)
                .ToListAsync();

            // For each feed, get one random article
            var recentArticles = new List<WebArticle>();
            var random = new Random();
            
            foreach (var feedId in recentFeedIds.OrderBy(_ => random.Next()))
            {
                var article = await db.WebArticles
                    .Include(a => a.Feed)
                    .Where(a => a.FeedId == feedId)
                    .OrderBy(_ => EF.Functions.Random())
                    .FirstOrDefaultAsync();
                    
                if (article != null)
                {
                    recentArticles.Add(article);
                    if (recentArticles.Count >= 5) break; // Limit to 5 articles
                }
            }

            if (recentArticles.Count > 0)
            {
                activities.Add(new DiscoveryActivity(
                    recentArticles.Select(x => new DiscoveryItem("article", x)).ToList()
                ).ToActivity());
            }
        }

        // Fetch a larger batch of recent posts to rank
        var postsQuery = db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(null, [], [], isListing: true)
            .Take(take * 5); // Fetch more posts to have a good pool for ranking

        var posts = await postsQuery.ToListAsync();
        posts = await ps.LoadPostInfo(posts, null, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount =
                reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

        // Rank and sort
        var now = SystemClock.Instance.GetCurrentInstant();
        var rankedPosts = posts
            .Select(p => new { Post = p, Rank = CalculateHotRank(p, now) })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Post)
            .Take(take)
            .ToList();

        // Formatting data
        foreach (var post in rankedPosts)
            activities.Add(post.ToActivity());

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }

    public async Task<List<Activity>> GetActivities(
        int take,
        Instant? cursor,
        Account.Account currentUser,
        string? filter = null,
        HashSet<string>? debugInclude = null
    )
    {
        var activities = new List<Activity>();
        var userFriends = await rels.ListAccountFriends(currentUser);
        var userPublishers = await pub.GetUserPublishers(currentUser.Id);
        debugInclude ??= new HashSet<string>();

        if (string.IsNullOrEmpty(filter))
        {
            if (cursor == null && (debugInclude.Contains("realms") || Random.Shared.NextDouble() < 0.2))
            {
                var realms = await ds.GetPublicRealmsAsync(null, null, 5, 0, true);
                if (realms.Count > 0)
                {
                    activities.Add(new DiscoveryActivity(
                        realms.Select(x => new DiscoveryItem("realm", x)).ToList()
                    ).ToActivity());
                }
            }

            if (cursor == null && (debugInclude.Contains("publishers") || Random.Shared.NextDouble() < 0.2))
            {
                var popularPublishers = await GetPopularPublishers(5);
                if (popularPublishers.Count > 0)
                {
                    activities.Add(new DiscoveryActivity(
                        popularPublishers.Select(x => new DiscoveryItem("publisher", x)).ToList()
                    ).ToActivity());
                }
            }
            
            if (debugInclude.Contains("articles") || Random.Shared.NextDouble() < 0.2)
            {
                var recentArticlesQuery = db.WebArticles
                    .Take(20); // Get a larger pool for randomization

                // Apply random ordering 50% of the time
                if (Random.Shared.NextDouble() < 0.5)
                    recentArticlesQuery = recentArticlesQuery.OrderBy(_ => EF.Functions.Random());
                else
                    recentArticlesQuery = recentArticlesQuery.OrderByDescending(a => a.PublishedAt);

                var recentArticles = await recentArticlesQuery.Take(5).ToListAsync();

                if (recentArticles.Count > 0)
                {
                    activities.Add(new DiscoveryActivity(
                        recentArticles.Select(x => new DiscoveryItem("article", x)).ToList()
                    ).ToActivity());
                }
            }
        }

        // Get publishers based on filter
        var filteredPublishers = filter switch
        {
            "subscriptions" => await pub.GetSubscribedPublishers(currentUser.Id),
            "friends" => (await pub.GetUserPublishersBatch(userFriends)).SelectMany(x => x.Value)
                .DistinctBy(x => x.Id)
                .ToList(),
            _ => null
        };

        var filteredPublishersId = filteredPublishers?.Select(e => e.Id).ToList();

        // Build the query based on the filter
        var postsQuery = db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .AsQueryable();

        if (filteredPublishersId is not null)
            postsQuery = postsQuery.Where(p => filteredPublishersId.Contains(p.PublisherId));

        // Complete the query with visibility filtering and execute
        var posts = await postsQuery
            .FilterWithVisibility(currentUser, userFriends, filter is null ? userPublishers : [], isListing: true)
            .Take(take * 5) // Fetch more posts to have a good pool for ranking
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, currentUser, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
        {
            post.ReactionsCount =
                reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

            // Track view for each post in the feed
            await ps.IncreaseViewCount(post.Id, currentUser.Id.ToString());
        }

        // Rank and sort
        var now = SystemClock.Instance.GetCurrentInstant();
        var rankedPosts = posts
            .Select(p => new { Post = p, Rank = CalculateHotRank(p, now) })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Post)
            .Take(take)
            .ToList();

        // Formatting data
        foreach (var post in rankedPosts)
            activities.Add(post.ToActivity());

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }

    private static double CalculatePopularity(List<Post.Post> posts)
    {
        var score = posts.Sum(p => p.Upvotes - p.Downvotes);
        var postCount = posts.Count;
        return score + postCount;
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

        var rankedPublishers = publishers
            .Select(p => new
            {
                Publisher = p,
                Rank = CalculatePopularity(posts.Where(post => post.PublisherId == p.Id).ToList())
            })
            .OrderByDescending(x => x.Rank)
            .Select(x => x.Publisher)
            .Take(take)
            .ToList();

        return rankedPublishers;
    }
}
