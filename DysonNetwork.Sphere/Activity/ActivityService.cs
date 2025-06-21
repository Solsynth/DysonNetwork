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