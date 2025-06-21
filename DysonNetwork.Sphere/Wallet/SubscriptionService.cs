using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Wallet;

public class SubscriptionService(AppDatabase db, PaymentService payment, ICacheService cache)
{
    public async Task<Subscription> CreateSubscriptionAsync(
        Account.Account account,
        string identifier,
        string paymentMethod,
        PaymentDetails paymentDetails,
        Duration? cycleDuration = null,
        string? coupon = null,
        bool isFreeTrail = false,
        bool isAutoRenewal = true
    )
    {
        var subscriptionTemplate = SubscriptionTypeData
            .SubscriptionDict.TryGetValue(identifier, out var template)
            ? template
            : null;
        if (subscriptionTemplate is null)
            throw new ArgumentOutOfRangeException(nameof(identifier), $@"Subscription {identifier} was not found.");

        cycleDuration ??= Duration.FromDays(30);

        var existingSubscription = await GetSubscriptionAsync(account.Id, identifier);
        if (existingSubscription is not null)
        {
            throw new InvalidOperationException($"Active subscription with identifier {identifier} already exists.");
        }

        Coupon? couponData = null;
        if (coupon is not null)
        {
            var inputCouponId = Guid.TryParse(coupon, out var parsedCouponId) ? parsedCouponId : Guid.Empty;
            couponData = await db.WalletCoupons
                .Where(c => (c.Id == inputCouponId) || (c.Identifier != null && c.Identifier == coupon))
                .FirstOrDefaultAsync();
            if (couponData is null) throw new InvalidOperationException($"Coupon {coupon} was not found.");
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var subscription = new Subscription
        {
            BegunAt = now,
            EndedAt = now.Plus(cycleDuration.Value),
            Identifier = identifier,
            IsActive = true,
            IsFreeTrial = isFreeTrail,
            Status = SubscriptionStatus.Unpaid,
            PaymentMethod = paymentMethod,
            PaymentDetails = paymentDetails,
            BasePrice = subscriptionTemplate.BasePrice,
            CouponId = couponData?.Id,
            Coupon = couponData,
            RenewalAt = (isFreeTrail || !isAutoRenewal) ? null : now.Plus(cycleDuration.Value),
            AccountId = account.Id,
        };

        db.WalletSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    /// <summary>
    /// Cancel the renewal of the current activated subscription.
    /// </summary>
    /// <param name="accountId">The user who requested the action.</param>
    /// <param name="identifier">The subscription identifier</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The active subscription was not found</exception>
    public async Task<Subscription> CancelSubscriptionAsync(Guid accountId, string identifier)
    {
        var subscription = await GetSubscriptionAsync(accountId, identifier);
        if (subscription is null)
            throw new InvalidOperationException($"Subscription with identifier {identifier} was not found.");
        if (subscription.Status == SubscriptionStatus.Cancelled)
            throw new InvalidOperationException("Subscription is already cancelled.");

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.RenewalAt = null;

        await db.SaveChangesAsync();

        // Invalidate the cache for this subscription
        var cacheKey = $"{SubscriptionCacheKeyPrefix}{accountId}:{identifier}";
        await cache.RemoveAsync(cacheKey);

        return subscription;
    }

    public const string SubscriptionOrderIdentifier = "solian.subscription.order";

    /// <summary>
    /// Creates a subscription order for an unpaid or expired subscription.
    /// If the subscription is active, it will extend its expiration date.
    /// </summary>
    /// <param name="accountId">The unique identifier for the account associated with the subscription.</param>
    /// <param name="identifier">The unique subscription identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created subscription order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no matching unpaid or expired subscription is found.</exception>
    public async Task<Order> CreateSubscriptionOrder(Guid accountId, string identifier)
    {
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && s.Identifier == identifier)
            .Where(s => s.Status != SubscriptionStatus.Expired)
            .Include(s => s.Coupon)
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();
        if (subscription is null) throw new InvalidOperationException("No matching subscription found.");

        return await payment.CreateOrderAsync(
            null,
            WalletCurrency.GoldenPoint,
            subscription.FinalPrice,
            appIdentifier: SubscriptionOrderIdentifier,
            meta: new Dictionary<string, object>()
            {
                ["subscription_id"] = subscription.Id.ToString(),
                ["subscription_identifier"] = subscription.Identifier,
            }
        );
    }

    public async Task<Subscription> HandleSubscriptionOrder(Order order)
    {
        if (order.AppIdentifier != SubscriptionOrderIdentifier || order.Status != OrderStatus.Paid ||
            order.Meta?["subscription_id"] is not string subscriptionId)
            throw new InvalidOperationException("Invalid order.");

        var subscriptionIdParsed = Guid.TryParse(subscriptionId, out var parsedSubscriptionId)
            ? parsedSubscriptionId
            : Guid.Empty;
        if (subscriptionIdParsed == Guid.Empty)
            throw new InvalidOperationException("Invalid order.");
        var subscription = await db.WalletSubscriptions
            .Where(s => s.Id == subscriptionIdParsed)
            .Include(s => s.Coupon)
            .FirstOrDefaultAsync();
        if (subscription is null)
            throw new InvalidOperationException("Invalid order.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var cycle = subscription.BegunAt.Minus(subscription.RenewalAt ?? now);

        var nextRenewalAt = subscription.RenewalAt?.Plus(cycle);
        var nextEndedAt = subscription.RenewalAt?.Plus(cycle);

        subscription.Status = SubscriptionStatus.Paid;
        subscription.RenewalAt = nextRenewalAt;
        subscription.EndedAt = nextEndedAt;

        db.Update(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    private const string SubscriptionCacheKeyPrefix = "subscription:";

    public async Task<Subscription?> GetSubscriptionAsync(Guid accountId, string identifier)
    {
        // Create a unique cache key for this subscription
        var cacheKey = $"{SubscriptionCacheKeyPrefix}{accountId}:{identifier}";

        // Try to get the subscription from cache first
        var (found, cachedSubscription) = await cache.GetAsyncWithStatus<Subscription>(cacheKey);
        if (found && cachedSubscription != null)
        {
            return cachedSubscription;
        }

        // If not in cache, get from database
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && s.Identifier == identifier)
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();

        // Cache the result if found (with 30 minutes expiry)
        if (subscription != null)
        {
            await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(30));
        }

        return subscription;
    }
}