using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Live;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherSubscriptionService(
    AppDatabase db,
    Post.PostService ps,
    PublisherService pub,
    ILocalizationService localizer,
    ICacheService cache,
    DyRingService.DyRingServiceClient pusher,
    DyAccountService.DyAccountServiceClient accounts
)
{
    public class SubscriptionReadStatus
    {
        public SnPublisherSubscription Subscription { get; set; } = null!;
        public Instant? LatestContentAt { get; set; }
        public bool HasNewContent { get; set; }
    }

    /// <summary>
    /// Checks if an active subscription exists between the account and publisher
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>True if an active subscription exists, false otherwise</returns>
    public async Task<bool> SubscriptionExistsAsync(Guid accountId, Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .AnyAsync(p => p.AccountId == accountId &&
                            p.PublisherId == publisherId &&
                            p.EndedAt == null);
    }

    /// <summary>
    /// Gets an active subscription by account and publisher ID
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>The subscription or null if not found</returns>
    public async Task<SnPublisherSubscription?> GetSubscriptionAsync(Guid accountId, Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.AccountId == accountId && 
                                       p.PublisherId == publisherId && 
                                       p.EndedAt == null);
    }

    /// <summary>
    /// Gets a subscription by account and publisher ID, including ended ones
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>The subscription or null if not found</returns>
    public async Task<SnPublisherSubscription?> GetSubscriptionIncludingEndedAsync(Guid accountId, Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.PublisherId == publisherId);
    }

    /// <summary>
    /// Notifies all subscribers about a new post from a publisher
    /// </summary>
    /// <param name="post">The new post</param>
    /// <returns>The number of subscribers notified</returns>
    public async Task<int> NotifySubscriberPost(SnPost post)
    {
        if (!post.PublisherId.HasValue || post.Publisher is null)
            return 0;
        if (post.RepliedPostId is not null)
            return 0;
        if (post.Visibility != Shared.Models.PostVisibility.Public)
            return 0;
        if (post.Publisher.IsShadowbanned)
            return 0;

        var postsRequireFollow = await pub.HasPostsRequireFollowFlag(post.PublisherId.Value);
        if (postsRequireFollow)
        {
            return await NotifyFollowersPost(post);
        }

        // Create notification data
        var (title, message) = ps.ChopPostForNotification(post);

        // Gather subscribers
        var subscribers = await db.PublisherSubscriptions
            .Where(p => p.PublisherId == post.PublisherId)
            .ToListAsync();
        if (subscribers.Count == 0)
            return 0;

        List<SnPostCategorySubscription> categorySubscribers = [];
        if (post.Categories.Count > 0)
        {
            var categoryIds = post.Categories.Select(x => x.Id).ToList();
            var subs = await db.PostCategorySubscriptions
                .Where(s => s.CategoryId != null && categoryIds.Contains(s.CategoryId.Value))
                .ToListAsync();
            categorySubscribers.AddRange(subs);
        }

        if (post.Tags.Count > 0)
        {
            var tagIds = post.Tags.Select(x => x.Id).ToList();
            var subs = await db.PostCategorySubscriptions
                .Where(s => s.TagId != null && tagIds.Contains(s.TagId.Value))
                .ToListAsync();
            categorySubscribers.AddRange(subs);
        }

        List<string> requestAccountIds = [];
        requestAccountIds.AddRange(subscribers.Select(x => x.AccountId.ToString()));
        requestAccountIds.AddRange(categorySubscribers.Select(x => x.AccountId.ToString()));

        var queryRequest = new DyGetAccountBatchRequest();
        queryRequest.Id.AddRange(requestAccountIds.Distinct());
        var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

        // Notify each subscriber
        var notifiedCount = 0;
        foreach (var target in queryResponse.Accounts.GroupBy(x => x.Language))
        {
            try
            {
                var notification = ps.BuildPostNotification(
                    topic: "posts.new",
                    title: localizer.Get(
                        "postSubscriptionTitle",
                        locale: target.Key,
                        args: new { publisher = post.Publisher!.Nick, title }
                    ),
                    body: message,
                    post: post,
                    extraMeta: new Dictionary<string, object?>
                    {
                        ["notification_type"] = "subscription",
                    }
                );
                var request = new DySendPushNotificationToUsersRequest { Notification = notification };
                request.UserIds.AddRange(target.Select(x => x.Id.ToString()));
                await pusher.SendPushNotificationToUsersAsync(request);
                notifiedCount++;
            }
            catch (Exception)
            {
                // Log the error but continue with other notifications
                // We don't want one failed notification to stop the others
            }
        }

        return notifiedCount;
    }

    private async Task<int> NotifyFollowersPost(SnPost post)
    {
        var (title, message) = ps.ChopPostForNotification(post);

        var followers = await db.PublisherFollowRequests
            .Where(r => r.PublisherId == post.PublisherId && r.State == FollowRequestState.Accepted)
            .ToListAsync();
        if (followers.Count == 0)
            return 0;

        var requestAccountIds = followers.Select(x => x.AccountId.ToString()).ToList();

        var queryRequest = new DyGetAccountBatchRequest();
        queryRequest.Id.AddRange(requestAccountIds.Distinct());
        var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

        var notifiedCount = 0;
        foreach (var target in queryResponse.Accounts.GroupBy(x => x.Language))
        {
            try
            {
                var notification = ps.BuildPostNotification(
                    topic: "posts.new",
                    title: localizer.Get(
                        "postSubscriptionTitle",
                        locale: target.Key,
                        args: new { publisher = post.Publisher!.Nick, title }
                    ),
                    body: message,
                    post: post,
                    extraMeta: new Dictionary<string, object?>
                    {
                        ["notification_type"] = "subscription",
                        ["require_follow"] = true,
                    }
                );
                var request = new DySendPushNotificationToUsersRequest { Notification = notification };
                request.UserIds.AddRange(target.Select(x => x.Id.ToString()));
                await pusher.SendPushNotificationToUsersAsync(request);
                notifiedCount++;
            }
            catch (Exception)
            {
            }
        }

        return notifiedCount;
    }

    /// <summary>
    /// Notifies all subscribers when a live stream goes live
    /// </summary>
    /// <param name="liveStream">The live stream that went live</param>
    /// <returns>The number of subscribers notified</returns>
    public async Task<int> NotifySubscriberLiveStream(SnLiveStream liveStream)
    {
        if (!liveStream.PublisherId.HasValue || liveStream.Publisher is null)
            return 0;
        if (liveStream.Visibility != Shared.Models.LiveStreamVisibility.Public)
            return 0;

        var title = liveStream.Title ?? "Live Stream";
        var publisherName = liveStream.Publisher.Nick ?? "Publisher";

        var data = new Dictionary<string, object>
        {
            ["livestream_id"] = liveStream.Id.ToString(),
            ["publisher_id"] = liveStream.PublisherId.Value.ToString()
        };

        if (liveStream.Thumbnail is not null)
            data["image"] = liveStream.Thumbnail.Id;

        if (liveStream.Publisher.Picture is not null)
            data["pfp"] = liveStream.Publisher.Picture.Id;

        var subscribers = await db.PublisherSubscriptions
            .Where(p => p.PublisherId == liveStream.PublisherId)
            .ToListAsync();

        if (subscribers.Count == 0)
            return 0;

        var queryRequest = new DyGetAccountBatchRequest();
        queryRequest.Id.AddRange(subscribers.Select(x => x.AccountId.ToString()));
        var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

        var notifiedCount = 0;
        foreach (var target in queryResponse.Accounts.GroupBy(x => x.Language))
        {
            try
            {
                var notification = new DyPushNotification
                {
                    Topic = "livestream.started",
                    Title = localizer.Get("liveStreamStartedTitle", locale: target.Key, args: new { publisher = publisherName }),
                    Body = title,
                    Meta = InfraObjectCoder.ConvertObjectToByteString(data),
                    IsSavable = true,
                    ActionUri = $"/livestreams/{liveStream.Id}"
                };
                var request = new DySendPushNotificationToUsersRequest { Notification = notification };
                request.UserIds.AddRange(target.Select(x => x.Id.ToString()));
                await pusher.SendPushNotificationToUsersAsync(request);
                notifiedCount++;
            }
            catch (Exception)
            {
            }
        }

        return notifiedCount;
    }

    /// <summary>
    /// Gets all active subscriptions for an account
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>A list of active subscriptions</returns>
    public async Task<List<SnPublisherSubscription>> GetAccountSubscriptionsAsync(Guid accountId)
    {
        return await db.PublisherSubscriptions
            .Include(p => p.Publisher)
            .Where(p => p.AccountId == accountId)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all active subscribers for a publisher
    /// </summary>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>A list of active subscriptions</returns>
    public async Task<List<SnPublisherSubscription>> GetPublisherSubscribersAsync(Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Where(p => p.PublisherId == publisherId)
            .ToListAsync();
    }

    public async Task<Dictionary<Guid, Instant?>> GetLatestContentAtForPublishersAsync(
        IEnumerable<Guid> publisherIds
    )
    {
        var ids = publisherIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var latestPosts = await db.Posts
            .Where(p =>
                p.PublisherId.HasValue &&
                ids.Contains(p.PublisherId.Value) &&
                p.RepliedPostId == null &&
                p.Visibility == Shared.Models.PostVisibility.Public
            )
            .GroupBy(p => p.PublisherId!.Value)
            .Select(g => new
            {
                PublisherId = g.Key,
                LatestContentAt = g.Max(p => p.PublishedAt ?? p.CreatedAt)
            })
            .ToListAsync();

        var latestLiveStreams = await db.LiveStreams
            .Where(ls =>
                ls.PublisherId.HasValue &&
                ids.Contains(ls.PublisherId.Value) &&
                ls.Visibility == Shared.Models.LiveStreamVisibility.Public &&
                ls.StartedAt != null
            )
            .GroupBy(ls => ls.PublisherId!.Value)
            .Select(g => new
            {
                PublisherId = g.Key,
                LatestContentAt = g.Max(ls => ls.StartedAt ?? ls.CreatedAt)
            })
            .ToListAsync();

        var latestContentAt = ids.ToDictionary(id => id, _ => (Instant?)null);

        foreach (var item in latestPosts)
            latestContentAt[item.PublisherId] = item.LatestContentAt;

        foreach (var item in latestLiveStreams)
        {
            if (!latestContentAt.TryGetValue(item.PublisherId, out var current) ||
                current == null ||
                item.LatestContentAt > current)
            {
                latestContentAt[item.PublisherId] = item.LatestContentAt;
            }
        }

        return latestContentAt;
    }

    public async Task<SubscriptionReadStatus?> GetSubscriptionReadStatusAsync(
        Guid accountId,
        Guid publisherId
    )
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is null)
            return null;

        var latestContentAt = (await GetLatestContentAtForPublishersAsync([publisherId]))[publisherId];

        return new SubscriptionReadStatus
        {
            Subscription = subscription,
            LatestContentAt = latestContentAt,
            HasNewContent = latestContentAt != null &&
                            (subscription.LastReadAt == null || latestContentAt > subscription.LastReadAt)
        };
    }

    public async Task<SnPublisherSubscription?> UpdateLastReadAtAsync(
        Guid accountId,
        Guid publisherId,
        Instant? lastReadAt = null
    )
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is null)
            return null;

        subscription.LastReadAt = lastReadAt ?? SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return subscription;
    }

    /// <summary>
    /// Creates a new subscription between an account and a publisher
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>The created subscription</returns>
    public async Task<SnPublisherSubscription> CreateSubscriptionAsync(
        Guid accountId,
        Guid publisherId
    )
    {
        // Check if an active subscription already exists
        var existingSubscription = await GetSubscriptionAsync(accountId, publisherId);

        if (existingSubscription != null)
            return existingSubscription;

        // Check if a ended subscription exists - resume it if so
        var endedSubscription = await GetSubscriptionIncludingEndedAsync(accountId, publisherId);
        if (endedSubscription != null)
        {
            endedSubscription.EndedAt = null;
            endedSubscription.EndReason = null;
            endedSubscription.EndedByAccountId = null;
            endedSubscription.Notify = true;
            db.PublisherSubscriptions.Update(endedSubscription);
            await db.SaveChangesAsync();
            await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));
            return endedSubscription;
        }

        // Create a new subscription
        var subscription = new SnPublisherSubscription
        {
            AccountId = accountId,
            PublisherId = publisherId,
        };

        db.PublisherSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return subscription;
    }

    /// <summary>
    /// Marks a subscription as ended (user left voluntarily)
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>True if the subscription was ended, false if it wasn't found</returns>
    public async Task<bool> CancelSubscriptionAsync(Guid accountId, Guid publisherId)
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is null)
            return false;

        subscription.EndedAt = SystemClock.Instance.GetCurrentInstant();
        subscription.EndReason = SubscriptionEndReason.UserLeft;
        db.PublisherSubscriptions.Update(subscription);
        await db.SaveChangesAsync();

        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return true;
    }

    /// <summary>
    /// Adds a subscriber directly (manager action), bypassing approval flow
    /// </summary>
    /// <param name="accountId">The account ID to add</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>The subscription or error message</returns>
    public async Task<SnPublisherSubscription> AddSubscriberAsync(Guid accountId, Guid publisherId)
    {
        var existing = await GetSubscriptionIncludingEndedAsync(accountId, publisherId);
        if (existing != null && existing.EndedAt == null)
            throw new InvalidOperationException("Already subscribed");

        if (existing != null && existing.EndReason == SubscriptionEndReason.RemovedByPublisher)
            throw new InvalidOperationException("Account was removed by publisher and cannot be re-added");

        if (existing != null && existing.EndReason == SubscriptionEndReason.UserLeft)
            throw new InvalidOperationException("Account left voluntarily and cannot be re-added");

        if (existing != null)
        {
            existing.EndedAt = null;
            existing.EndReason = null;
            existing.EndedByAccountId = null;
            existing.Notify = false;
            db.PublisherSubscriptions.Update(existing);
        }
        else
        {
            var subscription = new SnPublisherSubscription
            {
                AccountId = accountId,
                PublisherId = publisherId,
                Notify = false,
            };
            db.PublisherSubscriptions.Add(subscription);
        }

        await db.SaveChangesAsync();
        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return await GetSubscriptionAsync(accountId, publisherId);
    }

    /// <summary>
    /// Removes a subscriber (manager action), preventing re-subscription
    /// </summary>
    /// <param name="accountId">The account ID to remove</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <param name="removedByAccountId">The manager account ID doing the removal</param>
    /// <returns>True if the subscriber was removed, false if not found</returns>
    public async Task<bool> RemoveSubscriberAsync(Guid accountId, Guid publisherId, Guid removedByAccountId)
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is null)
            return false;

        subscription.EndedAt = SystemClock.Instance.GetCurrentInstant();
        subscription.EndReason = SubscriptionEndReason.RemovedByPublisher;
        subscription.EndedByAccountId = removedByAccountId;
        db.PublisherSubscriptions.Update(subscription);
        await db.SaveChangesAsync();

        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return true;
    }

    /// <summary>
    /// Updates the notify setting for a subscription
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <param name="notify">Whether to notify on new posts</param>
    /// <returns>The updated subscription or null if not found</returns>
    public async Task<SnPublisherSubscription?> UpdateSubscriberNotifyAsync(Guid accountId, Guid publisherId, bool notify)
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is null)
            return null;

        subscription.Notify = notify;
        db.PublisherSubscriptions.Update(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }
}
