using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db, RelationshipService rels)
{
    public async Task<List<Activity>> GetActivitiesForAnyone(int take, Instant cursor)
    {
        var activities = new List<Activity>();
        
        // Crunching up data
        var posts = await db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null)
            .Where(p => p.CreatedAt > cursor)
            .FilterWithVisibility(null, [], isListing: true)
            .Take(take)
            .ToListAsync();
        
        // Formatting data
        foreach (var post in posts)
            activities.Add(post.ToActivity());

        return activities;
    }
    
    public async Task<List<Activity>> GetActivities(int take, Instant cursor, Account.Account currentUser)
    {
        var activities = new List<Activity>();
        var userFriends = await rels.ListAccountFriends(currentUser);
        
        // Crunching data
        var posts = await db.Posts
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Where(e => e.RepliedPostId == null || e.RepliedPostId == currentUser.Id)
            .Where(p => p.CreatedAt > cursor)
            .FilterWithVisibility(currentUser, userFriends, isListing: true)
            .Take(take)
            .ToListAsync();
        
        // Formatting data
        foreach (var post in posts)
            activities.Add(post.ToActivity());

        if (activities.Count == 0)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            activities.Add(Activity.Empty());
        }

        return activities;
    }
}