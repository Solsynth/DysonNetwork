using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db, RelationshipService rels, PostService ps)
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
            .Where(p => cursor == null || cursor > p.CreatedAt)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(null, [], isListing: true)
            .Take(take)
            .ToListAsync();
        posts = PostService.TruncatePostContent(posts);
        posts = await ps.LoadPublishers(posts);

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
        
        // Crunching data
        var posts = await db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .Where(p => cursor == null || p.CreatedAt > cursor)
            .OrderByDescending(p => p.PublishedAt)
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .Take(take)
            .ToListAsync();
        posts = PostService.TruncatePostContent(posts);
        posts = await ps.LoadPublishers(posts);

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
}