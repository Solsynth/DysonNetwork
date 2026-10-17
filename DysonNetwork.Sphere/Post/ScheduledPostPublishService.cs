using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

/// <summary>
/// Publishes posts whose <c>published_at</c> has arrived. A post becomes "published"
/// as soon as <c>drafted_at</c> is null and <c>published_at</c> is in the past, but
/// the one-time side effects (subscriber notifications, federation, indexing,
/// realtime broadcast) are only dispatched when <c>publish_notified_at</c> is claimed.
/// Immediate publishes claim it inline; scheduled posts are claimed here.
/// </summary>
public class ScheduledPostPublishService(
    AppDatabase db,
    PostService posts,
    IEventBus eventBus,
    ILogger<ScheduledPostPublishService> logger
)
{
    public const int BatchSize = 100;

    public async Task<int> PublishDuePostsAsync(CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var duePostIds = await db
            .Posts.AsNoTracking()
            .Where(p =>
                p.PublishNotifiedAt == null
                && p.FediverseUri == null
                && p.DraftedAt == null
                && p.PublishedAt != null
                && p.PublishedAt <= now
            )
            .OrderBy(p => p.PublishedAt)
            .Take(BatchSize)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var publishedCount = 0;
        foreach (var postId in duePostIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claim the post atomically: only one worker may dispatch its side
            // effects. Re-check the publish state because the author may have
            // rescheduled or withdrawn it after the batch query ran.
            var claimed = await db
                .Posts.Where(p =>
                    p.Id == postId
                    && p.PublishNotifiedAt == null
                    && p.FediverseUri == null
                    && p.DraftedAt == null
                    && p.PublishedAt != null
                    && p.PublishedAt <= now
                )
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(p => p.PublishNotifiedAt, now)
                        .SetProperty(p => p.UpdatedAt, now),
                    cancellationToken
                );
            if (claimed == 0)
                continue;

            var post = await db
                .Posts.Include(p => p.Publisher)
                .Include(p => p.RepliedPost)
                .Include(p => p.Tags)
                .Include(p => p.Categories)
                .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);
            if (post is null)
                continue;

            await PublishRealmActivityAsync(post, cancellationToken);
            posts.DispatchPublishSideEffects(post, null);

            publishedCount++;
            logger.LogInformation(
                "Published scheduled post {PostId} of publisher {PublisherId}",
                post.Id,
                post.PublisherId
            );
        }

        return publishedCount;
    }

    private async Task PublishRealmActivityAsync(SnPost post, CancellationToken cancellationToken)
    {
        if (!post.RealmId.HasValue || post.Publisher?.AccountId is not { } publisherAccountId)
            return;

        try
        {
            await eventBus.PublishAsync(
                new RealmActivityEvent
                {
                    RealmId = post.RealmId.Value,
                    AccountId = publisherAccountId,
                    ActivityType = "post_created",
                    ReferenceId = post.Id.ToString(),
                    Delta = 20,
                },
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error when publishing realm activity for scheduled post {PostId}",
                post.Id
            );
        }
    }
}
