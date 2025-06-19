using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db, PublisherService pub, RelationshipService rels, PostService ps)
{
    public async Task<List<Activity>> GetActivitiesForAnyone(int take, Instant? cursor)
    {
        var activities = new List<Activity>();

        // Crunching up data
        var posts = await db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(null, [], [], isListing: true)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, null, true);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount =
                reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

        // Formatting data
        foreach (var post in posts)
            activities.Add(post.ToActivity());

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }

    public async Task<List<Activity>> GetActivities(int take, Instant? cursor, Account.Account currentUser)
    {
        var activities = new List<Activity>();
        var userFriends = await rels.ListAccountFriends(currentUser);
        var userPublishers = await pub.GetUserPublishers(currentUser.Id);
        
        var publishersId = userPublishers.Select(e => e.Id).ToList();
        
        // Crunching data
        var posts = await db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null || publishersId.Contains(e.RepliedPost!.PublisherId))
            .Where(p => cursor == null || p.PublishedAt < cursor)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true)
            .Take(take)
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

        // Formatting data
        foreach (var post in posts)
            activities.Add(post.ToActivity());

        if (activities.Count == 0)
            activities.Add(Activity.Empty());

        return activities;
    }
}