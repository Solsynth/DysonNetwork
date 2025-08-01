using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherSubscriptionService(
    AppDatabase db,
    PostService ps,
    IStringLocalizer<NotificationResource> localizer,
    ICacheService cache,
    PusherService.PusherServiceClient pusher,
    AccountService.AccountServiceClient accounts
)
{
    /// <summary>
    /// Checks if a subscription exists between the account and publisher
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>True if a subscription exists, false otherwise</returns>
    public async Task<bool> SubscriptionExistsAsync(Guid accountId, Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .AnyAsync(ps => ps.AccountId == accountId &&
                            ps.PublisherId == publisherId &&
                            ps.Status == PublisherSubscriptionStatus.Active);
    }

    /// <summary>
    /// Gets a subscription by account and publisher ID
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>The subscription or null if not found</returns>
    public async Task<PublisherSubscription?> GetSubscriptionAsync(Guid accountId, Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Include(ps => ps.Publisher)
            .FirstOrDefaultAsync(ps => ps.AccountId == accountId && ps.PublisherId == publisherId);
    }

    /// <summary>
    /// Notifies all subscribers about a new post from a publisher
    /// </summary>
    /// <param name="post">The new post</param>
    /// <returns>The number of subscribers notified</returns>
    public async Task<int> NotifySubscriberPost(Post.Post post)
    {
        if (post.RepliedPostId is not null)
            return 0;
        if (post.Visibility != PostVisibility.Public)
            return 0;

        var subscribers = await db.PublisherSubscriptions
            .Where(p => p.PublisherId == post.PublisherId &&
                        p.Status == PublisherSubscriptionStatus.Active)
            .ToListAsync();
        if (subscribers.Count == 0)
            return 0;

        // Create notification data
        var (title, message) = ps.ChopPostForNotification(post);

        // Data to include with the notification
        var data = new Dictionary<string, object>
        {
            { "post_id", post.Id.ToString() },
            { "publisher_id", post.Publisher.Id.ToString() }
        };


        var queryRequest = new GetAccountBatchRequest();
        queryRequest.Id.AddRange(subscribers.DistinctBy(s => s.AccountId).Select(m => m.AccountId.ToString()));
        var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

        var notification = new PushNotification
        {
            Topic = "posts.new",
            Title = localizer["PostSubscriptionTitle", post.Publisher.Name, title],
            Body = message,
            Meta = GrpcTypeHelper.ConvertObjectToByteString(data),
            IsSavable = true,
            ActionUri = $"/posts/{post.Id}"
        };

        // Notify each subscriber
        var notifiedCount = 0;
        foreach (var target in queryResponse.Accounts)
        {
            try
            {
                CultureService.SetCultureInfo(target);
                await pusher.SendPushNotificationToUserAsync(
                    new SendPushNotificationToUserRequest
                    {
                        UserId = target.Id,
                        Notification = notification
                    }
                );
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

    /// <summary>
    /// Gets all active subscriptions for an account
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>A list of active subscriptions</returns>
    public async Task<List<PublisherSubscription>> GetAccountSubscriptionsAsync(Guid accountId)
    {
        return await db.PublisherSubscriptions
            .Include(ps => ps.Publisher)
            .Where(ps => ps.AccountId == accountId && ps.Status == PublisherSubscriptionStatus.Active)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all active subscribers for a publisher
    /// </summary>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>A list of active subscriptions</returns>
    public async Task<List<PublisherSubscription>> GetPublisherSubscribersAsync(Guid publisherId)
    {
        return await db.PublisherSubscriptions
            .Where(ps => ps.PublisherId == publisherId && ps.Status == PublisherSubscriptionStatus.Active)
            .ToListAsync();
    }

    /// <summary>
    /// Creates a new subscription between an account and a publisher
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <param name="tier">Optional subscription tier</param>
    /// <returns>The created subscription</returns>
    public async Task<PublisherSubscription> CreateSubscriptionAsync(
        Guid accountId,
        Guid publisherId,
        int tier = 0
    )
    {
        // Check if a subscription already exists
        var existingSubscription = await GetSubscriptionAsync(accountId, publisherId);

        if (existingSubscription != null)
        {
            // If it exists but is not active, reactivate it
            if (existingSubscription.Status == PublisherSubscriptionStatus.Active) return existingSubscription;
            existingSubscription.Status = PublisherSubscriptionStatus.Active;
            existingSubscription.Tier = tier;

            await db.SaveChangesAsync();
            return existingSubscription;

            // If it's already active, just return it
        }

        // Create a new subscription
        var subscription = new PublisherSubscription
        {
            AccountId = accountId,
            PublisherId = publisherId,
            Status = PublisherSubscriptionStatus.Active,
            Tier = tier,
        };

        db.PublisherSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return subscription;
    }

    /// <summary>
    /// Cancels a subscription
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <param name="publisherId">The publisher ID</param>
    /// <returns>True if the subscription was cancelled, false if it wasn't found</returns>
    public async Task<bool> CancelSubscriptionAsync(Guid accountId, Guid publisherId)
    {
        var subscription = await GetSubscriptionAsync(accountId, publisherId);
        if (subscription is not { Status: PublisherSubscriptionStatus.Active })
            return false;

        subscription.Status = PublisherSubscriptionStatus.Cancelled;
        await db.SaveChangesAsync();

        await cache.RemoveAsync(string.Format(PublisherService.SubscribedPublishersCacheKey, accountId));

        return true;
    }
}