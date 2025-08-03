using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Pass.Wallet.PaymentHandlers;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using AccountService = DysonNetwork.Pass.Account.AccountService;
using Duration = NodaTime.Duration;

namespace DysonNetwork.Pass.Wallet;

public class SubscriptionService(
    AppDatabase db,
    PaymentService payment,
    AccountService accounts,
    PusherService.PusherServiceClient pusher,
    IStringLocalizer<NotificationResource> localizer,
    IConfiguration configuration,
    ICacheService cache,
    ILogger<SubscriptionService> logger
)
{
    public async Task<Subscription> CreateSubscriptionAsync(
        Account.Account account,
        string identifier,
        string paymentMethod,
        PaymentDetails paymentDetails,
        Duration? cycleDuration = null,
        string? coupon = null,
        bool isFreeTrial = false,
        bool isAutoRenewal = true,
        bool noop = false
    )
    {
        var subscriptionInfo = SubscriptionTypeData
            .SubscriptionDict.TryGetValue(identifier, out var template)
            ? template
            : null;
        if (subscriptionInfo is null)
            throw new ArgumentOutOfRangeException(nameof(identifier), $@"Subscription {identifier} was not found.");
        var subscriptionsInGroup = subscriptionInfo.GroupIdentifier is not null
            ? SubscriptionTypeData.SubscriptionDict
                .Where(s => s.Value.GroupIdentifier == subscriptionInfo.GroupIdentifier)
                .Select(s => s.Value.Identifier)
                .ToArray()
            : [identifier];

        cycleDuration ??= Duration.FromDays(30);

        var existingSubscription = await GetSubscriptionAsync(account.Id, subscriptionsInGroup);
        if (existingSubscription is not null && !noop)
            throw new InvalidOperationException($"Active subscription with identifier {identifier} already exists.");
        if (existingSubscription is not null)
            return existingSubscription;

        if (subscriptionInfo.RequiredLevel > 0)
        {
            var profile = await db.AccountProfiles
                .Where(p => p.AccountId == account.Id)
                .FirstOrDefaultAsync();
            if (profile is null) throw new InvalidOperationException("Account profile was not found.");
            if (profile.Level < subscriptionInfo.RequiredLevel)
                throw new InvalidOperationException(
                    $"Account level must be at least {subscriptionInfo.RequiredLevel} to subscribe to {identifier}."
                );
        }

        if (isFreeTrial)
        {
            var prevFreeTrial = await db.WalletSubscriptions
                .Where(s => s.AccountId == account.Id && s.Identifier == identifier && s.IsFreeTrial)
                .FirstOrDefaultAsync();
            if (prevFreeTrial is not null)
                throw new InvalidOperationException("Free trial already exists.");
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
            IsFreeTrial = isFreeTrial,
            Status = SubscriptionStatus.Unpaid,
            PaymentMethod = paymentMethod,
            PaymentDetails = paymentDetails,
            BasePrice = subscriptionInfo.BasePrice,
            CouponId = couponData?.Id,
            Coupon = couponData,
            RenewalAt = (isFreeTrial || !isAutoRenewal) ? null : now.Plus(cycleDuration.Value),
            AccountId = account.Id,
        };

        db.WalletSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    public async Task<Subscription> CreateSubscriptionFromOrder(ISubscriptionOrder order)
    {
        var cfgSection = configuration.GetSection("Payment:Subscriptions");
        var provider = order.Provider;

        var currency = "irl";
        var subscriptionIdentifier = order.SubscriptionId;
        switch (provider)
        {
            case "afdian":
                // Get the Afdian section first, then bind it to a dictionary
                var afdianPlans = cfgSection.GetSection("Afdian").Get<Dictionary<string, string>>();
                logger.LogInformation("Afdian plans configuration: {Plans}", JsonSerializer.Serialize(afdianPlans));
                if (afdianPlans != null && afdianPlans.TryGetValue(subscriptionIdentifier, out var planName))
                    subscriptionIdentifier = planName;
                currency = "cny";
                break;
        }

        var subscriptionTemplate = SubscriptionTypeData
            .SubscriptionDict.TryGetValue(subscriptionIdentifier, out var template)
            ? template
            : null;
        if (subscriptionTemplate is null)
            throw new ArgumentOutOfRangeException(nameof(subscriptionIdentifier),
                $@"Subscription {subscriptionIdentifier} was not found.");

        Account.Account? account = null;
        if (!string.IsNullOrEmpty(provider))
            account = await accounts.LookupAccountByConnection(order.AccountId, provider);
        else if (Guid.TryParse(order.AccountId, out var accountId))
            account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);

        if (account is null)
            throw new InvalidOperationException($"Account was not found with identifier {order.AccountId}");

        var cycleDuration = order.Duration;

        var existingSubscription = await GetSubscriptionAsync(account.Id, subscriptionIdentifier);
        if (existingSubscription is not null && existingSubscription.PaymentMethod != provider)
            throw new InvalidOperationException(
                $"Active subscription with identifier {subscriptionIdentifier} already exists.");
        if (existingSubscription?.PaymentDetails.OrderId == order.Id)
            return existingSubscription;
        if (existingSubscription is not null)
        {
            // Same provider, but different order, renew the subscription
            existingSubscription.PaymentDetails.OrderId = order.Id;
            existingSubscription.EndedAt = order.BegunAt.Plus(cycleDuration);
            existingSubscription.RenewalAt = order.BegunAt.Plus(cycleDuration);
            existingSubscription.Status = SubscriptionStatus.Active;

            db.Update(existingSubscription);
            await db.SaveChangesAsync();

            return existingSubscription;
        }

        var subscription = new Subscription
        {
            BegunAt = order.BegunAt,
            EndedAt = order.BegunAt.Plus(cycleDuration),
            IsActive = true,
            Status = SubscriptionStatus.Active,
            Identifier = subscriptionIdentifier,
            PaymentMethod = provider,
            PaymentDetails = new PaymentDetails
            {
                Currency = currency,
                OrderId = order.Id,
            },
            BasePrice = subscriptionTemplate.BasePrice,
            RenewalAt = order.BegunAt.Plus(cycleDuration),
            AccountId = account.Id,
        };

        db.WalletSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        await NotifySubscriptionBegun(subscription);

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
        if (subscription.Status != SubscriptionStatus.Active)
            throw new InvalidOperationException("Subscription is already cancelled.");
        if (subscription.RenewalAt is null)
            throw new InvalidOperationException("Subscription is no need to be cancelled.");
        if (subscription.PaymentMethod != SubscriptionPaymentMethod.InAppWallet)
            throw new InvalidOperationException(
                "Only in-app wallet subscription can be cancelled. For other payment methods, please head to the payment provider."
            );

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

        var subscriptionInfo = SubscriptionTypeData.SubscriptionDict
            .TryGetValue(subscription.Identifier, out var template)
            ? template
            : null;
        if (subscriptionInfo is null) throw new InvalidOperationException("No matching subscription found.");

        return await payment.CreateOrderAsync(
            null,
            subscriptionInfo.Currency,
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
            order.Meta?["subscription_id"] is not JsonElement subscriptionIdJson)
            throw new InvalidOperationException("Invalid order.");

        var subscriptionId = Guid.TryParse(subscriptionIdJson.ToString(), out var parsedSubscriptionId)
            ? parsedSubscriptionId
            : Guid.Empty;
        if (subscriptionId == Guid.Empty)
            throw new InvalidOperationException("Invalid order.");
        var subscription = await db.WalletSubscriptions
            .Where(s => s.Id == subscriptionId)
            .Include(s => s.Coupon)
            .FirstOrDefaultAsync();
        if (subscription is null)
            throw new InvalidOperationException("Invalid order.");

        if (subscription.Status == SubscriptionStatus.Expired)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var cycle = subscription.BegunAt.Minus(subscription.RenewalAt ?? subscription.EndedAt ?? now);

            var nextRenewalAt = subscription.RenewalAt?.Plus(cycle);
            var nextEndedAt = subscription.EndedAt?.Plus(cycle);

            subscription.RenewalAt = nextRenewalAt;
            subscription.EndedAt = nextEndedAt;
        }

        subscription.Status = SubscriptionStatus.Active;

        db.Update(subscription);
        await db.SaveChangesAsync();

        await NotifySubscriptionBegun(subscription);

        return subscription;
    }

    /// <summary>
    /// Updates the status of expired subscriptions to reflect their current state.
    /// This helps maintain accurate subscription records and is typically called periodically.
    /// </summary>
    /// <param name="batchSize">Maximum number of subscriptions to process</param>
    /// <returns>Number of subscriptions that were marked as expired</returns>
    public async Task<int> UpdateExpiredSubscriptionsAsync(int batchSize = 100)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        // Find active subscriptions that have passed their end date
        var expiredSubscriptions = await db.WalletSubscriptions
            .Where(s => s.IsActive)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .Where(s => s.EndedAt.HasValue && s.EndedAt.Value < now)
            .Take(batchSize)
            .ToListAsync();

        if (expiredSubscriptions.Count == 0)
            return 0;

        foreach (var subscription in expiredSubscriptions)
        {
            subscription.Status = SubscriptionStatus.Expired;

            // Clear the cache for this subscription
            var cacheKey = $"{SubscriptionCacheKeyPrefix}{subscription.AccountId}:{subscription.Identifier}";
            await cache.RemoveAsync(cacheKey);
        }

        await db.SaveChangesAsync();
        return expiredSubscriptions.Count;
    }

    private async Task NotifySubscriptionBegun(Subscription subscription)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == subscription.AccountId);
        if (account is null) return;

        AccountService.SetCultureInfo(account);

        var humanReadableName =
            SubscriptionTypeData.SubscriptionHumanReadable.TryGetValue(subscription.Identifier, out var humanReadable)
                ? humanReadable
                : subscription.Identifier;
        var duration = subscription.EndedAt is not null
            ? subscription.EndedAt.Value.Minus(subscription.BegunAt).Days.ToString()
            : "infinite";

        var notification = new PushNotification
        {
            Topic = "subscriptions.begun",
            Title = localizer["SubscriptionAppliedTitle", humanReadableName],
            Body = localizer["SubscriptionAppliedBody", duration, humanReadableName],
            Meta = GrpcTypeHelper.ConvertObjectToByteString(new Dictionary<string, object>
            {
                ["subscription_id"] = subscription.Id.ToString()
            }),
            IsSavable = true
        };
        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = account.Id.ToString(),
                Notification = notification
            }
        );
    }

    private const string SubscriptionCacheKeyPrefix = "subscription:";

    public async Task<Subscription?> GetSubscriptionAsync(Guid accountId, params string[] identifiers)
    {
        // Create a unique cache key for this subscription
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(string.Join(",", identifiers)));
        var hashIdentifier = Convert.ToHexStringLower(hashBytes);

        var cacheKey = $"{SubscriptionCacheKeyPrefix}{accountId}:{hashIdentifier}";

        // Try to get the subscription from cache first
        var (found, cachedSubscription) = await cache.GetAsyncWithStatus<Subscription>(cacheKey);
        if (found && cachedSubscription != null)
        {
            return cachedSubscription;
        }

        // If not in cache, get from database
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && identifiers.Contains(s.Identifier))
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();

        // Cache the result if found (with 5 minutes expiry)
        if (subscription != null)
            await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(5));

        return subscription;
    }

    private const string SubscriptionPerkCacheKeyPrefix = "subscription:perk:";

    private static readonly List<string> PerkIdentifiers =
        [SubscriptionType.Stellar, SubscriptionType.Nova, SubscriptionType.Supernova];

    public async Task<Subscription?> GetPerkSubscriptionAsync(Guid accountId)
    {
        var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{accountId}";

        // Try to get the subscription from cache first
        var (found, cachedSubscription) = await cache.GetAsyncWithStatus<Subscription>(cacheKey);
        if (found && cachedSubscription != null)
        {
            return cachedSubscription;
        }

        // If not in cache, get from database
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && PerkIdentifiers.Contains(s.Identifier))
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();

        // Cache the result if found (with 30 minutes expiry)
        if (subscription != null)
            await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(30));

        return subscription;
    }


    public async Task<Dictionary<Guid, Subscription?>> GetPerkSubscriptionsAsync(List<Guid> accountIds)
    {
        var result = new Dictionary<Guid, Subscription?>();
        var missingAccountIds = new List<Guid>();

        // Try to get the subscription from cache first
        foreach (var accountId in accountIds)
        {
            var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{accountId}";
            var (found, cachedSubscription) = await cache.GetAsyncWithStatus<Subscription>(cacheKey);
            if (found && cachedSubscription != null)
                result[accountId] = cachedSubscription;
            else
                missingAccountIds.Add(accountId);
        }

        if (missingAccountIds.Count <= 0) return result;

        // If not in cache, get from database
        var subscriptions = await db.WalletSubscriptions
            .Where(s => missingAccountIds.Contains(s.AccountId))
            .Where(s => PerkIdentifiers.Contains(s.Identifier))
            .ToListAsync();

        // Group the subscriptions by account id
        foreach (var subscription in subscriptions)
        {
            result[subscription.AccountId] = subscription;

            // Cache the result if found (with 30 minutes expiry)
            var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{subscription.AccountId}";
            await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(30));
        }

        return result;
    }
}