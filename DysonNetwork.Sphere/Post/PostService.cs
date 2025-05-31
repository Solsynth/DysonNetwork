using System.Text.Json;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Activity;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PostService(
    AppDatabase db,
    FileService fs,
    ActivityService act,
    IStringLocalizer<NotificationResource> localizer,
    NotificationService nty,
    IServiceScopeFactory factory
)
{
    private const string PostFileUsageIdentifier = "post";

    public static List<Post> TruncatePostContent(List<Post> input)
    {
        const int maxLength = 256;
        foreach (var item in input)
        {
            if (!(item.Content?.Length > maxLength)) continue;
            item.Content = item.Content[..maxLength];
            item.IsTruncated = true;
        }

        return input;
    }

    public async Task<Post> PostAsync(
        Account.Account user,
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot create a post with barely no content.");

        if (post.PublishedAt is not null)
        {
            if (post.PublishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot create the post which published in the past.");
        }
        else
        {
            post.PublishedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        }

        if (attachments is not null)
        {
            post.Attachments = await db.Files.Where(e => attachments.Contains(e.Id)).ToListAsync();
            // Re-order the list to match the id list places
            post.Attachments = attachments
                .Select(id => post.Attachments.First(a => a.Id == id))
                .ToList();
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        }

        db.Posts.Add(post);
        await db.SaveChangesAsync();
        await fs.MarkUsageRangeAsync(post.Attachments, 1);
        await fs.SetUsageRangeAsync(post.Attachments, PostFileUsageIdentifier);

        await act.CreateNewPostActivity(user, post);

        if (post.PublishedAt is not null && post.PublishedAt.Value.ToDateTimeUtc() <= DateTime.UtcNow)
            _ = Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                var pubSub = scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await pubSub.NotifySubscribersPostAsync(post);
            });

        return post;
    }

    public async Task<Post> UpdatePostAsync(
        Post post,
        List<string>? attachments = null,
        List<string>? tags = null,
        List<string>? categories = null,
        Instant? publishedAt = null
    )
    {
        if (post.Empty)
            throw new InvalidOperationException("Cannot edit a post to barely no content.");

        post.EditedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        if (publishedAt is not null)
        {
            // User cannot set the published at to the past to prevent scam,
            // But we can just let the controller set the published at, because when no changes to
            // the published at will blocked the update operation
            if (publishedAt.Value.ToDateTimeUtc() < DateTime.UtcNow)
                throw new InvalidOperationException("Cannot set the published at to the past.");
        }

        if (attachments is not null)
        {
            post.Attachments = (await fs.DiffAndMarkFilesAsync(attachments, post.Attachments)).current;
            await fs.DiffAndSetUsageAsync(attachments, PostFileUsageIdentifier);
        }

        if (tags is not null)
        {
            var existingTags = await db.PostTags.Where(e => tags.Contains(e.Slug)).ToListAsync();

            // Determine missing slugs
            var existingSlugs = existingTags.Select(t => t.Slug).ToHashSet();
            var missingSlugs = tags.Where(slug => !existingSlugs.Contains(slug)).ToList();

            var newTags = missingSlugs.Select(slug => new PostTag { Slug = slug }).ToList();
            if (newTags.Count > 0)
            {
                await db.PostTags.AddRangeAsync(newTags);
                await db.SaveChangesAsync();
            }

            post.Tags = existingTags.Concat(newTags).ToList();
        }

        if (categories is not null)
        {
            post.Categories = await db.PostCategories.Where(e => categories.Contains(e.Slug)).ToListAsync();
            if (post.Categories.Count != categories.Distinct().Count())
                throw new InvalidOperationException("Categories contains one or more categories that wasn't exists.");
        }

        db.Update(post);
        await db.SaveChangesAsync();

        return post;
    }

    public async Task DeletePostAsync(Post post)
    {
        db.Posts.Remove(post);
        await db.SaveChangesAsync();
        await fs.MarkUsageRangeAsync(post.Attachments, -1);
    }

    /// <summary>
    /// Calculate the total number of votes for a post.
    /// This function helps you save the new reactions.
    /// </summary>
    /// <param name="post">Post that modifying</param>
    /// <param name="reaction">The new / target reaction adding / removing</param>
    /// <param name="op">The original poster account of this post</param>
    /// <param name="isRemoving">Indicate this operation is adding / removing</param>
    /// <param name="isSelfReact">Indicate this reaction is by the original post himself</param>
    /// <param name="sender">The account that creates this reaction</param>
    public async Task<bool> ModifyPostVotes(
        Post post,
        PostReaction reaction,
        Account.Account sender,
        Account.Account? op,
        bool isRemoving,
        bool isSelfReact
    )
    {
        var isExistingReaction = await db.Set<PostReaction>()
            .AnyAsync(r => r.PostId == post.Id && r.AccountId == reaction.AccountId);

        if (isRemoving)
            await db.PostReactions
                .Where(r => r.PostId == post.Id && r.Symbol == reaction.Symbol && r.AccountId == reaction.AccountId)
                .ExecuteDeleteAsync();
        else
            db.PostReactions.Add(reaction);

        if (isExistingReaction)
        {
            if (!isRemoving)
                await db.SaveChangesAsync();
            return isRemoving;
        }

        if (isSelfReact)
        {
            await db.SaveChangesAsync();
            return isRemoving;
        }

        switch (reaction.Attitude)
        {
            case PostReactionAttitude.Positive:
                if (isRemoving) post.Upvotes--;
                else post.Upvotes++;
                break;
            case PostReactionAttitude.Negative:
                if (isRemoving) post.Downvotes--;
                else post.Downvotes++;
                break;
        }

        await db.SaveChangesAsync();

        if (!isSelfReact && op is not null)
        {
            await nty.SendNotification(
                op,
                "posts.reactions.new",
                localizer["PostReactTitle", sender.Nick],
                null,
                string.IsNullOrWhiteSpace(post.Title)
                    ? localizer["PostReactBody", sender.Nick, reaction.Symbol]
                    : localizer["PostReactContentBody", sender.Nick, reaction.Symbol,
                        post.Title]
            );
        }

        return isRemoving;
    }

    public async Task<Dictionary<string, int>> GetPostReactionMap(Guid postId)
    {
        return await db.Set<PostReaction>()
            .Where(r => r.PostId == postId)
            .GroupBy(r => r.Symbol)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count()
            );
    }

    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetPostReactionMapBatch(List<Guid> postIds)
    {
        return await db.Set<PostReaction>()
            .Where(r => postIds.Contains(r.PostId))
            .GroupBy(r => r.PostId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.GroupBy(r => r.Symbol)
                    .ToDictionary(
                        sg => sg.Key,
                        sg => sg.Count()
                    )
            );
    }

    public async Task<List<Post>> LoadPublishers(List<Post> posts)
    {
        var publisherIds = posts
            .Where(e => e.Publisher.AccountId != null)
            .SelectMany<Post, Guid?>(e =>
            [
                e.Publisher.Id,
                e.RepliedPost?.Publisher.Id,
                e.ForwardedPost?.Publisher.Id,
                e.ThreadedPost?.Publisher.Id
            ])
            .Where(e => e != null)
            .Distinct()
            .ToList();
        if (publisherIds.Count == 0) return posts;

        var publishers = await db.Publishers
            .Where(e => e.AccountId != null && publisherIds.Contains(e.AccountId.Value))
            .ToDictionaryAsync(e => e.AccountId!.Value);

        foreach (var post in posts)
        {
            if (publishers.TryGetValue(post.Publisher.Id, out var publisher))
                post.Publisher = publisher;

            if (post.RepliedPost?.Publisher.Id != null &&
                publishers.TryGetValue(post.RepliedPost.Publisher.Id, out var repliedPublisher))
                post.RepliedPost.Publisher = repliedPublisher;

            if (post.ForwardedPost?.Publisher.Id != null && 
                publishers.TryGetValue(post.ForwardedPost.Publisher.Id, out var forwardedPublisher))
                post.ForwardedPost.Publisher = forwardedPublisher;

            if (post.ThreadedPost?.Publisher.Id != null &&
                publishers.TryGetValue(post.ThreadedPost.Publisher.Id, out var threadedPublisher))
                post.ThreadedPost.Publisher = threadedPublisher;
        }

        return posts;
    }
}

public static class PostQueryExtensions
{
    public static IQueryable<Post> FilterWithVisibility(this IQueryable<Post> source, Account.Account? currentUser,
        List<Guid> userFriends, bool isListing = false)
    {
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        source = isListing switch
        {
            true when currentUser is not null => source.Where(e =>
                e.Visibility != PostVisibility.Unlisted || e.Publisher.AccountId == currentUser.Id),
            true => source.Where(e => e.Visibility != PostVisibility.Unlisted),
            _ => source
        };

        if (currentUser is null)
            return source
                .Where(e => e.PublishedAt != null && now >= e.PublishedAt)
                .Where(e => e.Visibility == PostVisibility.Public);

        return source
            .Where(e => (e.PublishedAt != null && now >= e.PublishedAt) || e.Publisher.AccountId == currentUser.Id)
            .Where(e => e.Visibility != PostVisibility.Private || e.Publisher.AccountId == currentUser.Id)
            .Where(e => e.Visibility != PostVisibility.Friends ||
                        (e.Publisher.AccountId != null && userFriends.Contains(e.Publisher.AccountId.Value)) ||
                        e.Publisher.AccountId == currentUser.Id);
    }
}