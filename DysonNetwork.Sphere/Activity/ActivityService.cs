using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public class ActivityService(AppDatabase db)
{
    public async Task<List<Activity>> LoadActivityData(List<Activity> input, Account.Account? currentUser,
        List<long> userFriends)
    {
        if (input.Count == 0) return input;

        var postsId = input
            .Where(e => e.ResourceIdentifier.StartsWith("posts/"))
            .Select(e => long.Parse(e.ResourceIdentifier.Split("/").Last()))
            .Distinct()
            .ToList();

        if (postsId.Count > 0)
        {
            var posts = await db.Posts.Where(e => postsId.Contains(e.Id))
                .Include(e => e.Publisher)
                .Include(e => e.Publisher.Picture)
                .Include(e => e.Publisher.Background)
                .Include(e => e.ThreadedPost)
                .Include(e => e.ForwardedPost)
                .Include(e => e.Attachments)
                .Include(e => e.Categories)
                .Include(e => e.Tags)
                .FilterWithVisibility(currentUser, userFriends)
                .ToListAsync();
            posts = PostService.TruncatePostContent(posts);

            var postsDict = posts.ToDictionary(p => p.Id);

            for (var idx = 0; idx < input.Count; idx++)
            {
                var resourceIdentifier = input[idx].ResourceIdentifier;
                if (!resourceIdentifier.StartsWith("posts/")) continue;
                var postId = long.Parse(resourceIdentifier.Split("/").Last());
                if (postsDict.TryGetValue(postId, out var post) && input[idx].Data is null)
                {
                    input[idx].Data = post;
                }
            }
        }

        return input;
    }

    public async Task<Activity> CreateActivity(
        Account.Account user,
        string type,
        string identifier,
        ActivityVisibility visibility = ActivityVisibility.Public,
        List<long>? visibleUsers = null
    )
    {
        var activity = new Activity
        {
            Type = type,
            ResourceIdentifier = identifier,
            Visibility = visibility,
            AccountId = user.Id,
            UsersVisible = visibleUsers ?? []
        };

        db.Activities.Add(activity);
        await db.SaveChangesAsync();

        return activity;
    }

    public async Task CreateNewPostActivity(Account.Account user, Post.Post post)
    {
        if (post.Visibility is PostVisibility.Unlisted or PostVisibility.Private) return;

        var identifier = $"posts/{post.Id}";
        if (post.RepliedPostId is not null)
        {
            var ogPost = await db.Posts.Where(e => e.Id == post.RepliedPostId).Include(e => e.Publisher)
                .FirstOrDefaultAsync();
            if (ogPost == null) return;
            await CreateActivity(
                user,
                "posts.new.replies",
                identifier,
                ActivityVisibility.Selected,
                [ogPost.Publisher.AccountId!.Value]
            );
        }

        await CreateActivity(
            user,
            "posts.new",
            identifier,
            post.Visibility == PostVisibility.Friends ? ActivityVisibility.Friends : ActivityVisibility.Public
        );
    }
}

public static class ActivityQueryExtensions
{
    public static IQueryable<Activity> FilterWithVisibility(this IQueryable<Activity> source,
        Account.Account? currentUser, List<long> userFriends)
    {
        if (currentUser is null)
            return source.Where(e => e.Visibility == ActivityVisibility.Public);

        return source
            .Where(e => e.Visibility != ActivityVisibility.Friends ||
                        userFriends.Contains(e.AccountId) ||
                        e.AccountId == currentUser.Id)
            .Where(e => e.Visibility != ActivityVisibility.Selected || e.UsersVisible.Contains(currentUser.Id));
    }
}