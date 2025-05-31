using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Activity;

public class ActivityReaderService(AppDatabase db, PostService ps)
{
    public async Task<List<Activity>> LoadActivityData(List<Activity> input, Account.Account? currentUser,
        List<Guid> userFriends)
    {
        if (input.Count == 0) return input;

        var postsId = input
            .Where(e => e.ResourceIdentifier.StartsWith("posts/"))
            .Select(e => Guid.Parse(e.ResourceIdentifier.Split("/").Last()))
            .Distinct()
            .ToList();
        if (postsId.Count > 0)
        {
            var posts = await db.Posts.Where(e => postsId.Contains(e.Id))
                .Include(e => e.ThreadedPost)
                .Include(e => e.ForwardedPost)
                .Include(e => e.Attachments)
                .Include(e => e.Categories)
                .Include(e => e.Tags)
                .FilterWithVisibility(currentUser, userFriends)
                .ToListAsync();
            posts = PostService.TruncatePostContent(posts);
            posts = await ps.LoadPublishers(posts);

            var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
            foreach (var post in posts)
                post.ReactionsCount =
                    reactionMaps.TryGetValue(post.Id, out var count) ? count : new Dictionary<string, int>();

            var postsDict = posts.ToDictionary(p => p.Id);

            foreach (var item in input)
            {
                var resourceIdentifier = item.ResourceIdentifier;
                if (!resourceIdentifier.StartsWith("posts/")) continue;
                var postId = Guid.Parse(resourceIdentifier.Split("/").Last());
                if (postsDict.TryGetValue(postId, out var post) && item.Data is null)
                {
                    item.Data = post;
                }
            }
        }

        var statusesId = input
            .Where(e => e.ResourceIdentifier.StartsWith("account.statuses/"))
            .Select(e => Guid.Parse(e.ResourceIdentifier.Split("/").Last()))
            .Distinct()
            .ToList();
        if (statusesId.Count > 0)
        {
            var statuses = await db.AccountStatuses.Where(e => statusesId.Contains(e.Id))
                .Include(e => e.Account)
                .Include(e => e.Account.Profile)
                .ToListAsync();
            var statusesDict = statuses.ToDictionary(p => p.Id);

            foreach (var item in input)
            {
                var resourceIdentifier = item.ResourceIdentifier;
                if (!resourceIdentifier.StartsWith("account.statuses/")) continue;
                var statusId = Guid.Parse(resourceIdentifier.Split("/").Last());
                if (statusesDict.TryGetValue(statusId, out var status) && item.Data is null)
                {
                    item.Data = status;
                }
            }
        }

        var checkInId = input
            .Where(e => e.ResourceIdentifier.StartsWith("account.check-in/"))
            .Select(e => Guid.Parse(e.ResourceIdentifier.Split("/").Last()))
            .Distinct()
            .ToList();
        if (checkInId.Count > 0)
        {
            var checkIns = await db.AccountCheckInResults.Where(e => checkInId.Contains(e.Id))
                .Include(e => e.Account)
                .Include(e => e.Account.Profile)
                .ToListAsync();
            var checkInsDict = checkIns.ToDictionary(p => p.Id);

            foreach (var item in input)
            {
                var resourceIdentifier = item.ResourceIdentifier;
                if (!resourceIdentifier.StartsWith("account.check-in/")) continue;
                var checkInResultId = Guid.Parse(resourceIdentifier.Split("/").Last());
                if (checkInsDict.TryGetValue(checkInResultId, out var checkIn) && item.Data is null)
                {
                    item.Data = checkIn;
                }
            }
        }

        return input;
    }
}

public class ActivityService(AppDatabase db)
{
    public async Task<Activity> CreateActivity(
        Account.Account user,
        string type,
        string identifier,
        ActivityVisibility visibility = ActivityVisibility.Public,
        List<Guid>? visibleUsers = null
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
            var ogPost = await db.Posts
                .Where(e => e.Id == post.RepliedPostId)
                .Include(e => e.Publisher)
                .FirstOrDefaultAsync();
            if (ogPost?.Publisher.AccountId == null) return;
            await CreateActivity(
                user,
                "posts.new.replies",
                identifier,
                ActivityVisibility.Selected,
                [ogPost.Publisher.AccountId!.Value]
            );
            return;
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
        Account.Account? currentUser, List<Guid> userFriends)
    {
        if (currentUser is null)
            return source.Where(e => e.Visibility == ActivityVisibility.Public);

        return source
            .Where(e => e.Visibility != ActivityVisibility.Friends ||
                        userFriends.Contains(e.AccountId) ||
                        e.AccountId == currentUser.Id)
            .Where(e => e.Visibility != ActivityVisibility.Selected ||
                        EF.Functions.JsonExists(e.UsersVisible, currentUser.Id.ToString()));
    }
}