
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Discovery;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db, PublisherService pub, RelationshipService rels, PostService ps, DiscoveryService ds)
{
    private double CalculateHotRank(Post.Post post, Instant now)
    {
        var score = post.Upvotes - post.Downvotes;
        var postTime = post.PublishedAt ?? post.CreatedAt;
        var hours = (now - postTime).TotalHours;
        // Add 1 to score to prevent negative results for posts with more downvotes than upvotes
        return (score + 1) / Math.Pow(hours + 2, 1.8);
    }

    public async Task<List<Activity>> GetActivitiesForAnyone(int take, Instant? cursor)
    {
        var activities = new List<Activity>();

        if (cursor == null)
        {
            var realms = await ds.GetPublicRealmsAsync(null, null);
            if (realms.Count > 0)
            {
                activities.Add(new DiscoveryActivity("Explore Realms", realms.Cast<object>().ToList()).ToActivity());
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
        string? filter = null
    )
    {
        var activities = new List<Activity>();
        var userFriends = await rels.ListAccountFriends(currentUser);
        var userPublishers = await pub.GetUserPublishers(currentUser.Id);

        if (cursor == null)
        {
            var realms = await ds.GetPublicRealmsAsync(null, null);
            if (realms.Count > 0)
            {
                activities.Add(new DiscoveryActivity("Explore Realms", realms.Cast<object>().ToList()).ToActivity());
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
}
