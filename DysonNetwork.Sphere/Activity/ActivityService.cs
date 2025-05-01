using DysonNetwork.Sphere.Post;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db)
{
    public async Task<Activity> CreateActivity(
        Account.Account user,
        string type,
        string identifier,
        ActivityVisibility visibility = ActivityVisibility.Public
    )
    {
        var activity = new Activity
        {
            Type = type,
            ResourceIdentifier = identifier,
            Visibility = visibility,
            AccountId = user.Id,
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        return activity;
    }

    public async Task CreateNewPostActivity(Account.Account user, Post.Post post)
    {
        if (post.Visibility is PostVisibility.Unlisted or PostVisibility.Private) return;

        var identifier = $"posts/{post.Id}";
        await CreateActivity(user, "posts.new", identifier,
            post.Visibility == PostVisibility.Friends ? ActivityVisibility.Friends : ActivityVisibility.Public);
    }
}

public static class ActivityQueryExtensions
{
    public static IQueryable<Activity> FilterWithVisibility(this IQueryable<Activity> source,
        Account.Account? currentUser, List<long> userFriends)
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        if (currentUser is null)
            return source.Where(e => e.Visibility == ActivityVisibility.Public);

        return source
            .Where(e => e.Visibility != ActivityVisibility.Friends ||
                        userFriends.Contains(e.AccountId) ||
                        e.AccountId == currentUser.Id);
    }
}