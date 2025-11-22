using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherSubscriptionService(
    AppDatabase db,
    Post.PostService ps,
    IStringLocalizer<NotificationResource> localizer,
    ICacheService cache,
    RingService.RingServiceClient pusher,
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
    public async Task<SnPublisherSubscription?> GetSubscriptionAsync(Guid accountId, Guid publisherId)
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
    public async Task<int> NotifySubscriberPost(SnPost post)
    {
        if (post.RepliedPostId is not null)
            return 0;
        if (post.Visibility != Shared.Models.PostVisibility.Public)
            return 0;

        // Create notification data
        var (title, message) = ps.ChopPostForNotification(post);

        // Data to include with the notification
        var images = new List<string>();
        if (post.Attachments.Any(p => p.MimeType?.StartsWith("image/") ?? false))
            images.AddRange(
                post.Attachments
                    .Where(p => p.MimeType?.StartsWith("image/") ?? false)
                    .Select(p => p.Id)
            );
        else if (post.Publisher.Picture is not null) images.Add(post.Publisher.Picture.Id);

        var data = new Dictionary<string, object>
        {
            ["post_id"] = post.Id,
            ["publisher_id"] = post.Publisher.Id.ToString(),
            ["images"] = images,
        };

        // Gather subscribers
        var subscribers = await db.PublisherSubscriptions
            .Where(p => p.PublisherId == post.PublisherId &&
                        p.Status == PublisherSubscriptionStatus.Active)
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

        var queryRequest = new GetAccountBatchRequest();
        queryRequest.Id.AddRange(requestAccountIds.Distinct());
        var queryResponse = await accounts.GetAccountBatchAsync(queryRequest);

        // Notify each subscriber
        var notifiedCount = 0;
        foreach (var target in queryResponse.Accounts.GroupBy(x => x.Language))
        {
            try
            {
                CultureService.SetCultureInfo(target.Key);
                var notification = new PushNotification
                {
                    Topic = "posts.new",
                    Title = localizer["PostSubscriptionTitle", post.Publisher.Nick, title],
                    Body = message,
                    Meta = GrpcTypeHelper.ConvertObjectToByteString(data),
                    IsSavable = true,
                    ActionUri = $"/posts/{post.Id}"
                };
                var request = new SendPushNotificationToUsersRequest { Notification = notification };
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

    /// <summary>
    /// Gets all active subscriptions for an account
    /// </summary>
    /// <param name="accountId">The account ID</param>
    /// <returns>A list of active subscriptions</returns>
    public async Task<List<SnPublisherSubscription>> GetAccountSubscriptionsAsync(Guid accountId)
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
    public async Task<List<SnPublisherSubscription>> GetPublisherSubscribersAsync(Guid publisherId)
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
    public async Task<SnPublisherSubscription> CreateSubscriptionAsync(
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
        var subscription = new SnPublisherSubscription
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