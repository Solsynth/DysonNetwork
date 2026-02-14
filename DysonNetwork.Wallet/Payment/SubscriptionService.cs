using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Wallet.Localization;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using Duration = NodaTime.Duration;
using GiftStatus = DysonNetwork.Shared.Models.GiftStatus;
using SubscriptionStatus = DysonNetwork.Shared.Models.SubscriptionStatus;

namespace DysonNetwork.Wallet.Payment;

public class SubscriptionService(
    AppDatabase db,
    PaymentService payment,
    AccountService.AccountServiceClient accounts,
    RingService.RingServiceClient pusher,
    ILocalizationService localizer,
    IConfiguration configuration,
    ICacheService cache,
    ILogger<SubscriptionService> logger
)
{
    public async Task<SnWalletSubscription> CreateSubscriptionAsync(
        Account account,
        string identifier,
        string paymentMethod,
        SnPaymentDetails paymentDetails,
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

        var accountId = Guid.Parse(account.Id);
        var existingSubscription = await GetSubscriptionAsync(accountId, subscriptionsInGroup);
        if (existingSubscription is not null && !noop)
            throw new InvalidOperationException($"Active subscription with identifier {identifier} already exists.");
        if (existingSubscription is not null)
            return existingSubscription;

        // Batch database queries for coupon and free trial check
        var prevFreeTrialTask = isFreeTrial
            ? db.WalletSubscriptions.FirstOrDefaultAsync(s =>
                s.AccountId == accountId && s.Identifier == identifier && s.IsFreeTrial)
            : Task.FromResult((SnWalletSubscription?)null);

        Guid couponGuidId = Guid.TryParse(coupon ?? "", out var parsedId) ? parsedId : Guid.Empty;
        var couponTask = coupon != null
            ? db.WalletCoupons.FirstOrDefaultAsync(c =>
                c.Id == couponGuidId || (c.Identifier != null && c.Identifier == coupon))
            : Task.FromResult((SnWalletCoupon?)null);

        // Await batched queries
        var prevFreeTrial = await prevFreeTrialTask;
        var couponData = await couponTask;

        // Validation checks
        if (isFreeTrial && prevFreeTrial != null)
            throw new InvalidOperationException("Free trial already exists.");

        if (coupon != null && couponData is null)
            throw new InvalidOperationException($"Coupon {coupon} was not found.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var subscription = new SnWalletSubscription
        {
            BegunAt = now,
            EndedAt = now.Plus(cycleDuration.Value),
            Identifier = identifier,
            IsActive = true,
            IsFreeTrial = isFreeTrial,
            Status = Shared.Models.SubscriptionStatus.Unpaid,
            PaymentMethod = paymentMethod,
            PaymentDetails = paymentDetails,
            BasePrice = subscriptionInfo.BasePrice,
            CouponId = couponData?.Id,
            Coupon = couponData,
            RenewalAt = (isFreeTrial || !isAutoRenewal) ? null : now.Plus(cycleDuration.Value),
            AccountId = accountId,
        };

        db.WalletSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return subscription;
    }

    public async Task<SnWalletSubscription> CreateSubscriptionFromOrder(ISubscriptionOrder order)
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
            .SubscriptionDict.GetValueOrDefault(subscriptionIdentifier);
        if (subscriptionTemplate is null)
            throw new ArgumentOutOfRangeException(nameof(subscriptionIdentifier),
                $"Subscription {subscriptionIdentifier} was not found.");

        SnAccount? account = null;
        if (!string.IsNullOrEmpty(provider))
        {
            // Use GetAccount instead of LookupAccountByConnection since that method may not exist
            if (Guid.TryParse(order.AccountId, out var accountId))
            {
                var accountProto = await accounts.GetAccountAsync(new GetAccountRequest { Id = accountId.ToString() });
                if (accountProto != null)
                    account = SnAccount.FromProtoValue(accountProto);
            }
        }
        else if (Guid.TryParse(order.AccountId, out var accountId))
        {
            var accountProto = await accounts.GetAccountAsync(new GetAccountRequest { Id = accountId.ToString() });
            if (accountProto != null)
                account = SnAccount.FromProtoValue(accountProto);
        }

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

        var subscription = new SnWalletSubscription
        {
            BegunAt = order.BegunAt,
            EndedAt = order.BegunAt.Plus(cycleDuration),
            IsActive = true,
            Status = SubscriptionStatus.Active,
            Identifier = subscriptionIdentifier,
            PaymentMethod = provider,
            PaymentDetails = new SnPaymentDetails
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

        await HandleSponsorCurrencyUpdateAsync(subscription);
        await HandleSponsorBadgeSubscriptionAsync(subscription);

        return subscription;
    }

    /// <summary>
    /// Cancel the renewal of the current activated subscription.
    /// </summary>
    /// <param name="accountId">The user who requested the action.</param>
    /// <param name="identifier">The subscription identifier</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">The active subscription was not found</exception>
    public async Task<SnWalletSubscription> CancelSubscriptionAsync(Guid accountId, string identifier)
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

    /// <summary>
    /// Creates a subscription order for an unpaid or expired subscription.
    /// If the subscription is active, it will extend its expiration date.
    /// </summary>
    /// <param name="accountId">The unique identifier for the account associated with the subscription.</param>
    /// <param name="identifier">The unique subscription identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created subscription order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no matching unpaid or expired subscription is found.</exception>
    public async Task<SnWalletOrder> CreateSubscriptionOrder(Guid accountId, string identifier)
    {
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && s.Identifier == identifier)
            .Where(s => s.Status != Shared.Models.SubscriptionStatus.Expired)
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
            appIdentifier: "internal",
            productIdentifier: identifier,
            meta: new Dictionary<string, object>()
            {
                ["subscription_id"] = subscription.Id.ToString(),
                ["subscription_identifier"] = subscription.Identifier,
            }
        );
    }

    /// <summary>
    /// Creates a gift order for an unpaid gift.
    /// </summary>
    /// <param name="accountId">The account ID of the gifter.</param>
    /// <param name="giftId">The unique identifier for the gift.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the created gift order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the gift is not found or not in payable status.</exception>
    public async Task<SnWalletOrder> CreateGiftOrder(Guid accountId, Guid giftId)
    {
        var gift = await db.WalletGifts
            .Where(g => g.Id == giftId && g.GifterId == accountId)
            .Where(g => g.Status == DysonNetwork.Shared.Models.GiftStatus.Created)
            .Include(g => g.Coupon)
            .FirstOrDefaultAsync();
        if (gift is null) throw new InvalidOperationException("No matching gift found.");

        var subscriptionInfo = SubscriptionTypeData.SubscriptionDict
            .TryGetValue(gift.SubscriptionIdentifier, out var template)
            ? template
            : null;
        if (subscriptionInfo is null) throw new InvalidOperationException("No matching subscription found.");

        return await payment.CreateOrderAsync(
            null,
            subscriptionInfo.Currency,
            gift.FinalPrice,
            appIdentifier: "gift",
            productIdentifier: gift.SubscriptionIdentifier,
            meta: new Dictionary<string, object>()
            {
                ["gift_id"] = gift.Id.ToString()
            }
        );
    }

    public async Task<SnWalletSubscription> HandleSubscriptionOrder(SnWalletOrder order)
    {
        if (order.Status != Shared.Models.OrderStatus.Paid ||
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

        if (subscription.Status == Shared.Models.SubscriptionStatus.Expired)
        {
            // Calculate original cycle duration and extend from the current ended date
            Duration originalCycle = subscription.EndedAt.Value - subscription.BegunAt;

            subscription.RenewalAt = subscription.RenewalAt.HasValue
                ? subscription.RenewalAt.Value.Plus(originalCycle)
                : subscription.EndedAt.Value.Plus(originalCycle);
            subscription.EndedAt = subscription.EndedAt.Value.Plus(originalCycle);
        }

        subscription.Status = Shared.Models.SubscriptionStatus.Active;

        db.Update(subscription);
        await db.SaveChangesAsync();

        await NotifySubscriptionBegun(subscription);

        return subscription;
    }

    public async Task<SnWalletGift> HandleGiftOrder(SnWalletOrder order)
    {
        if (order.Status != Shared.Models.OrderStatus.Paid || order.Meta?["gift_id"] is not JsonElement giftIdJson)
            throw new InvalidOperationException("Invalid order.");

        var giftId = Guid.TryParse(giftIdJson.ToString(), out var parsedGiftId)
            ? parsedGiftId
            : Guid.Empty;
        if (giftId == Guid.Empty)
            throw new InvalidOperationException("Invalid order.");
        var gift = await db.WalletGifts
            .Where(g => g.Id == giftId)
            .Include(g => g.Coupon)
            .FirstOrDefaultAsync();
        if (gift is null)
            throw new InvalidOperationException("Invalid order.");

        if (gift.Status != DysonNetwork.Shared.Models.GiftStatus.Created)
            throw new InvalidOperationException("Gift is not in payable status.");

        // Mark gift as sent after payment
        gift.Status = DysonNetwork.Shared.Models.GiftStatus.Sent;
        gift.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        db.Update(gift);
        await db.SaveChangesAsync();

        return gift;
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
            .Where(s => s.Status == Shared.Models.SubscriptionStatus.Active)
            .Where(s => s.EndedAt.HasValue && s.EndedAt.Value < now)
            .Take(batchSize)
            .ToListAsync();

        if (expiredSubscriptions.Count == 0)
            return 0;

        // Mark as expired
        foreach (var subscription in expiredSubscriptions)
        {
            subscription.Status = Shared.Models.SubscriptionStatus.Expired;
        }

        await db.SaveChangesAsync();

        // Batch invalidate caches for better performance
        var cacheTasks = expiredSubscriptions.Select(subscription =>
            cache.RemoveAsync($"{SubscriptionCacheKeyPrefix}{subscription.AccountId}:{subscription.Identifier}"));
        await Task.WhenAll(cacheTasks);

        return expiredSubscriptions.Count;
    }

    private async Task NotifySubscriptionBegun(SnWalletSubscription subscription)
    {
        var humanReadableName =
            SubscriptionTypeData.SubscriptionHumanReadable.TryGetValue(subscription.Identifier, out var humanReadable)
                ? humanReadable
                : subscription.Identifier;
        var duration = subscription.EndedAt is not null
            ? subscription.EndedAt.Value.Minus(subscription.BegunAt).Days.ToString()
            : "infinite";

        var locale = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var notification = new PushNotification
        {
            Topic = "subscriptions.begun",
            Title = localizer.Get("subscriptionAppliedTitle", locale: locale,
                args: new { subscriptionName = humanReadableName }),
            Body = localizer.Get("subscriptionAppliedBody", locale: locale,
                args: new { duration, subscription = humanReadableName }),
            Meta = InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
            {
                ["subscription_id"] = subscription.Id.ToString()
            }),
            IsSavable = true
        };
        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = subscription.AccountId.ToString(),
                Notification = notification
            }
        );
    }

    private const string SubscriptionCacheKeyPrefix = "subscription:";

    public async Task<SnWalletSubscription?> GetSubscriptionAsync(Guid accountId, params string[] identifiers)
    {
        // Create a unique cache key for this subscription
        var identifierPart = identifiers.Length == 1
            ? identifiers[0]
            : Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(string.Join(",", identifiers))));

        var cacheKey = $"{SubscriptionCacheKeyPrefix}{accountId}:{identifierPart}";

        // Try to get the subscription from cache first
        var (found, cachedSubscription) = await cache.GetAsyncWithStatus<SnWalletSubscription>(cacheKey);
        if (found && cachedSubscription != null)
        {
            return cachedSubscription;
        }

        // If not in cache, get from database
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && identifiers.Contains(s.Identifier))
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();
        if (subscription is { IsAvailable: false }) subscription = null;

        // Cache the result if found (with 5 minutes expiry)
        await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(5));

        return subscription;
    }

    private const string SubscriptionPerkCacheKeyPrefix = "subscription:perk:";

    private static readonly List<string> PerkIdentifiers =
        [SubscriptionType.Stellar, SubscriptionType.Nova, SubscriptionType.Supernova];

    public async Task<SnWalletSubscription?> GetPerkSubscriptionAsync(Guid accountId)
    {
        var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{accountId}";

        // Try to get the subscription from cache first
        var (found, cachedSubscription) = await cache.GetAsyncWithStatus<SnWalletSubscription>(cacheKey);
        if (found && cachedSubscription != null)
        {
            return cachedSubscription;
        }

        // If not in cache, get from database
        var now = SystemClock.Instance.GetCurrentInstant();
        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == accountId && PerkIdentifiers.Contains(s.Identifier))
            .Where(s => s.Status == Shared.Models.SubscriptionStatus.Active)
            .Where(s => s.EndedAt == null || s.EndedAt > now)
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();
        if (subscription is { IsAvailable: false }) subscription = null;

        // Cache the result if found (with 5 minutes expiry)
        await cache.SetAsync(cacheKey, subscription, TimeSpan.FromMinutes(5));

        return subscription;
    }


    public async Task<Dictionary<Guid, SnWalletSubscription?>> GetPerkSubscriptionsAsync(List<Guid> accountIds)
    {
        var result = new Dictionary<Guid, SnWalletSubscription?>();
        var missingAccountIds = new List<Guid>();

        // Try to get the subscription from cache first
        var cacheTasks = accountIds.Select(async accountId =>
        {
            var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{accountId}";
            var (found, cachedSubscription) = await cache.GetAsyncWithStatus<SnWalletSubscription>(cacheKey);
            return (accountId, found, cachedSubscription);
        });

        var cacheResults = await Task.WhenAll(cacheTasks);

        foreach (var (accountId, found, cachedSubscription) in cacheResults)
        {
            if (found && cachedSubscription != null)
                result[accountId] = cachedSubscription;
            else
                missingAccountIds.Add(accountId);
        }

        if (missingAccountIds.Count == 0) return result;

        // If not in cache, get from database
        var now = SystemClock.Instance.GetCurrentInstant();
        var subscriptions = await db.WalletSubscriptions
            .Where(s => missingAccountIds.Contains(s.AccountId))
            .Where(s => PerkIdentifiers.Contains(s.Identifier))
            .Where(s => s.Status == Shared.Models.SubscriptionStatus.Active)
            .Where(s => s.EndedAt == null || s.EndedAt > now)
            .OrderByDescending(s => s.BegunAt)
            .ToListAsync();

        // Group by account and select latest available subscription
        var groupedSubscriptions = subscriptions
            .Where(s => s.IsAvailable)
            .GroupBy(s => s.AccountId)
            .ToDictionary(g => g.Key, g => g.First());

        // Update results and batch cache operations
        var cacheSetTasks = new List<Task>();
        foreach (var kvp in groupedSubscriptions)
        {
            result[kvp.Key] = kvp.Value;
            var cacheKey = $"{SubscriptionPerkCacheKeyPrefix}{kvp.Key}";
            cacheSetTasks.Add(cache.SetAsync(cacheKey, kvp.Value, TimeSpan.FromMinutes(30)));
        }

        await Task.WhenAll(cacheSetTasks);

        return result;
    }

    /// <summary>
    /// Purchases a gift subscription that can be redeemed by another user.
    /// </summary>
    /// <param name="gifter">The account purchasing the gift.</param>
    /// <param name="recipientId">Optional specific recipient. If null, creates an open gift anyone can redeem.</param>
    /// <param name="subscriptionIdentifier">The subscription type being gifted.</param>
    /// <param name="paymentMethod">Payment method used by the gifter.</param>
    /// <param name="paymentDetails">Payment details from the gifter.</param>
    /// <param name="message">Optional personal message from the gifter.</param>
    /// <param name="coupon">Optional coupon code for discount.</param>
    /// <param name="giftDuration">How long the gift can be redeemed (default 30 days).</param>
    /// <param name="cycleDuration">The duration of the subscription once redeemed (default 30 days).</param>
    /// <returns>The created gift record.</returns>
    public async Task<SnWalletGift> PurchaseGiftAsync(
        Account gifter,
        Guid? recipientId,
        string subscriptionIdentifier,
        string paymentMethod,
        SnPaymentDetails paymentDetails,
        string? message = null,
        string? coupon = null,
        Duration? giftDuration = null,
        Duration? cycleDuration = null)
    {
        // Validate subscription exists
        var subscriptionInfo = SubscriptionTypeData
            .SubscriptionDict.TryGetValue(subscriptionIdentifier, out var template)
            ? template
            : null;
        if (subscriptionInfo is null)
            throw new ArgumentOutOfRangeException(nameof(subscriptionIdentifier),
                $@"Subscription {subscriptionIdentifier} was not found.");

        // Check if recipient account exists (if specified)
        Account? recipient = null;
        if (recipientId.HasValue)
        {
            var accountProto =
                await accounts.GetAccountAsync(new GetAccountRequest { Id = recipientId.Value.ToString() });
            if (accountProto != null)
                recipient = accountProto;
            if (recipient is null)
                throw new ArgumentOutOfRangeException(nameof(recipientId), "Recipient account not found.");
        }

        // Validate and get coupon if provided
        Guid couponGuidId = Guid.TryParse(coupon ?? "", out var parsedId) ? parsedId : Guid.Empty;
        var couponData = coupon != null
            ? await db.WalletCoupons.FirstOrDefaultAsync(c =>
                c.Id == couponGuidId || (c.Identifier != null && c.Identifier == coupon))
            : null;

        if (coupon != null && couponData is null)
            throw new InvalidOperationException($"Coupon {coupon} was not found.");

        // Set defaults
        giftDuration ??= Duration.FromDays(30); // Gift expires in 30 days
        cycleDuration ??= Duration.FromDays(30); // Subscription lasts 30 days once redeemed

        var now = SystemClock.Instance.GetCurrentInstant();

        // Generate unique gift code
        var giftCode = await GenerateUniqueGiftCodeAsync();

        // Calculate final price (with potential coupon discount)
        var tempSubscription = new SnWalletSubscription
        {
            BasePrice = subscriptionInfo.BasePrice,
            CouponId = couponData?.Id,
            Coupon = couponData,
            BegunAt = now // Need for price calculation
        };

        var finalPrice = tempSubscription.CalculateFinalPriceAt(now);

        var gift = new SnWalletGift
        {
            GifterId = Guid.Parse(gifter.Id),
            RecipientId = recipientId,
            GiftCode = giftCode,
            Message = message,
            SubscriptionIdentifier = subscriptionIdentifier,
            BasePrice = subscriptionInfo.BasePrice,
            FinalPrice = finalPrice,
            Status = DysonNetwork.Shared.Models.GiftStatus.Created,
            ExpiresAt = now.Plus(giftDuration.Value),
            IsOpenGift = !recipientId.HasValue,
            PaymentMethod = paymentMethod,
            PaymentDetails = paymentDetails,
            CouponId = couponData?.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WalletGifts.Add(gift);
        await db.SaveChangesAsync();

        return gift;
    }

    /// <summary>
    /// Activates a gift using the redemption code, creating a subscription for the redeemer.
    /// </summary>
    /// <param name="redeemer">The account redeeming the gift.</param>
    /// <param name="giftCode">The unique redemption code.</param>
    /// <returns>A tuple containing the activated gift and the created subscription.</returns>
    public async Task<(SnWalletGift Gift, SnWalletSubscription Subscription)> RedeemGiftAsync(
        Account redeemer,
        string giftCode)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var redeemerId = Guid.Parse(redeemer.Id);

        // Find and validate the gift
        var gift = await db.WalletGifts
            .Include(g => g.Coupon) // Include coupon for price calculation
            .FirstOrDefaultAsync(g => g.GiftCode == giftCode);

        if (gift is null)
            throw new InvalidOperationException("Gift code not found.");

        if (gift.Status != DysonNetwork.Shared.Models.GiftStatus.Sent)
            throw new InvalidOperationException("Gift is not available for redemption.");

        if (now > gift.ExpiresAt)
            throw new InvalidOperationException("Gift has expired.");

        if (gift.GifterId == redeemerId)
            throw new InvalidOperationException("You cannot redeem your own gift.");

        // Validate redeemer permissions
        if (!gift.IsOpenGift && gift.RecipientId != redeemerId)
            throw new InvalidOperationException("This gift is not intended for you.");

        // Check if redeemer already has this subscription type
        var subscriptionInfo = SubscriptionTypeData
            .SubscriptionDict.TryGetValue(gift.SubscriptionIdentifier, out var template)
            ? template
            : null;
        if (subscriptionInfo is null)
            throw new InvalidOperationException("Invalid gift subscription type.");

        var sameTypeSubscription = await GetSubscriptionAsync(redeemerId, gift.SubscriptionIdentifier);
        if (sameTypeSubscription is not null)
        {
            // Extend existing subscription
            var subscriptionDuration = Duration.FromDays(28);
            if (sameTypeSubscription.EndedAt.HasValue && sameTypeSubscription.EndedAt.Value > now)
            {
                sameTypeSubscription.EndedAt = sameTypeSubscription.EndedAt.Value.Plus(subscriptionDuration);
            }
            else
            {
                sameTypeSubscription.EndedAt = now.Plus(subscriptionDuration);
            }

            if (sameTypeSubscription.RenewalAt.HasValue)
            {
                sameTypeSubscription.RenewalAt = sameTypeSubscription.RenewalAt.Value.Plus(subscriptionDuration);
            }

            // Update gift status and link
            gift.Status = Shared.Models.GiftStatus.Redeemed;
            gift.RedeemedAt = now;
            gift.RedeemerId = redeemerId;
            gift.SubscriptionId = sameTypeSubscription.Id;
            gift.UpdatedAt = now;

            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.WalletSubscriptions.Update(sameTypeSubscription);
                db.WalletGifts.Update(gift);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            if (gift.GifterId == redeemerId) return (gift, sameTypeSubscription);
            await NotifyGiftClaimedByRecipient(gift, sameTypeSubscription, gift.GifterId, redeemer);

            return (gift, sameTypeSubscription);
        }

        var subscriptionsInGroup = subscriptionInfo.GroupIdentifier is not null
            ? SubscriptionTypeData.SubscriptionDict
                .Where(s => s.Value.GroupIdentifier == subscriptionInfo.GroupIdentifier)
                .Select(s => s.Value.Identifier)
                .ToArray()
            : [gift.SubscriptionIdentifier];

        var existingSubscription = await GetSubscriptionAsync(redeemerId, subscriptionsInGroup);
        if (existingSubscription is not null)
            throw new InvalidOperationException("You already have an active subscription of this type.");

        // We do not check account level requirement, since it is a gift

        // Create the subscription from the gift
        var cycleDuration = Duration.FromDays(28);
        var subscription = new SnWalletSubscription
        {
            BegunAt = now,
            EndedAt = now.Plus(cycleDuration),
            Identifier = gift.SubscriptionIdentifier,
            IsActive = true,
            IsFreeTrial = false,
            Status = Shared.Models.SubscriptionStatus.Active,
            PaymentMethod = "gift", // Special payment method indicating gift redemption
            PaymentDetails = new Shared.Models.SnPaymentDetails
            {
                Currency = "gift",
                OrderId = gift.Id.ToString()
            },
            BasePrice = gift.BasePrice,
            CouponId = gift.CouponId,
            Coupon = gift.Coupon,
            RenewalAt = now.Plus(cycleDuration),
            AccountId = redeemerId,
        };

        // Update the gift status
        gift.Status = DysonNetwork.Shared.Models.GiftStatus.Redeemed;
        gift.RedeemedAt = now;
        gift.RedeemerId = redeemerId;
        gift.Subscription = subscription;
        gift.UpdatedAt = now;

        // Save both gift and subscription
        using var createTransaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.WalletSubscriptions.Add(subscription);
            db.WalletGifts.Update(gift);
            await db.SaveChangesAsync();

            await createTransaction.CommitAsync();
        }
        catch
        {
            await createTransaction.RollbackAsync();
            throw;
        }

        // Send notification to gifter if different from redeemer
        if (gift.GifterId == redeemerId) return (gift, subscription);
        await NotifyGiftClaimedByRecipient(gift, subscription, gift.GifterId, redeemer);

        return (gift, subscription);
    }

    /// <summary>
    /// Retrieves a gift by its code (for redemption checking).
    /// </summary>
    public async Task<SnWalletGift?> GetGiftByCodeAsync(string giftCode)
    {
        return await db.WalletGifts
            .Include(g => g.Coupon)
            .FirstOrDefaultAsync(g => g.GiftCode == giftCode);
    }

    /// <summary>
    /// Retrieves gifts purchased by a specific account.
    /// Only returns gifts that have been sent or processed (not created/unpaid ones).
    /// </summary>
    public async Task<List<SnWalletGift>> GetGiftsByGifterAsync(Guid gifterId)
    {
        return await db.WalletGifts
            .Where(g => g.GifterId == gifterId && g.Status != Shared.Models.GiftStatus.Created)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SnWalletGift>> GetGiftsByRecipientAsync(Guid recipientId)
    {
        return await db.WalletGifts
            .Where(g => g.RecipientId == recipientId || (g.IsOpenGift && g.RedeemerId == recipientId))
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Marks a gift as sent (ready for redemption).
    /// </summary>
    public async Task<SnWalletGift> MarkGiftAsSentAsync(Guid giftId, Guid gifterId)
    {
        var gift = await db.WalletGifts.FirstOrDefaultAsync(g => g.Id == giftId && g.GifterId == gifterId);
        if (gift is null)
            throw new InvalidOperationException("Gift not found or access denied.");

        if (gift.Status != GiftStatus.Created)
            throw new InvalidOperationException("Gift cannot be marked as sent.");

        gift.Status = GiftStatus.Sent;
        gift.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        return gift;
    }

    /// <summary>
    /// Cancels a gift before it's redeemed.
    /// </summary>
    public async Task<SnWalletGift> CancelGiftAsync(Guid giftId, Guid gifterId)
    {
        var gift = await db.WalletGifts.FirstOrDefaultAsync(g => g.Id == giftId && g.GifterId == gifterId);
        if (gift is null)
            throw new InvalidOperationException("Gift not found or access denied.");

        if (gift.Status != DysonNetwork.Shared.Models.GiftStatus.Created &&
            gift.Status != DysonNetwork.Shared.Models.GiftStatus.Sent)
            throw new InvalidOperationException("Gift cannot be cancelled.");

        gift.Status = DysonNetwork.Shared.Models.GiftStatus.Cancelled;
        gift.UpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        return gift;
    }

    private async Task<string> GenerateUniqueGiftCodeAsync()
    {
        const int maxAttempts = 10;
        const int codeLength = 12;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Generate a random code
            var code = GenerateRandomCode(codeLength);

            // Check if it already exists
            var existingGift = await db.WalletGifts.FirstOrDefaultAsync(g => g.GiftCode == code);
            if (existingGift is null)
                return code;
        }

        throw new InvalidOperationException("Unable to generate unique gift code.");
    }

    private static string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[Random.Shared.Next(chars.Length)];
        }

        return new string(result);
    }

    private async Task NotifyGiftClaimedByRecipient(SnWalletGift gift, SnWalletSubscription subscription, Guid gifterId,
        Account redeemer)
    {
        var humanReadableName =
            SubscriptionTypeData.SubscriptionHumanReadable.TryGetValue(subscription.Identifier, out var humanReadable)
                ? humanReadable
                : subscription.Identifier;

        var locale = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var notification = new PushNotification
        {
            Topic = "gifts.claimed",
            Title = localizer.Get("giftClaimedTitle", locale: locale),
            Body = localizer.Get("giftClaimedBody", locale: locale,
                args: new { subscription = humanReadableName, user = redeemer.Name }),
            Meta = InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object>
            {
                ["gift_id"] = gift.Id.ToString(),
                ["subscription_id"] = subscription.Id.ToString(),
                ["redeemer_id"] = redeemer.Id
            }),
            IsSavable = true
        };

        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = gifterId.ToString(),
                Notification = notification
            }
        );
    }

    private async Task HandleSponsorCurrencyUpdateAsync(SnWalletSubscription subscription)
    {
        var amount = PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(subscription.Identifier) * 10;

        try
        {
            await payment.CreateTransactionWithAccountAsync(
                null,
                subscription.AccountId,
                WalletCurrency.GoldenPoint,
                amount,
                remarks: localizer.Get("stellarProgramPerkGoldenPoints")
            );
        }
        catch (Exception err)
        {
            logger.LogWarning("Unable to send golden points to member: {Error}", err);
        }
    }

    /// <summary>
    /// Handles sponsor page subscription by incrementing the sponsor badge level.
    /// </summary>
    /// <param name="subscription">The sponsor page subscription that was created.</param>
    private async Task HandleSponsorBadgeSubscriptionAsync(SnWalletSubscription subscription)
    {
        try
        {
            // Get the account's existing sponsor badges using the AccountService
            var listBadgesRequest = new ListBadgesRequest
            {
                AccountId = subscription.AccountId.ToString(),
                Type = "sponsor",
                ActiveOnly = false
            };

            var listBadgesResponse = await accounts.ListBadgesAsync(listBadgesRequest);
            var sponsorBadges = listBadgesResponse.Badges
                .Where(b => b.Type == "sponsor")
                .OrderByDescending(b => b.ActivatedAt)
                .ToList();

            var newLevel = PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(subscription.Identifier);
            if (sponsorBadges.Count > 0)
            {
                // Increment the level from the existing badge
                var latestBadge = sponsorBadges.First();
                if (latestBadge.Meta != null && latestBadge.Meta.TryGetValue("level", out var levelValue))
                {
                    if (int.TryParse(levelValue?.ToString(), out var currentLevel))
                        newLevel = currentLevel + 1;
                }
            }

            // Create new sponsor badge
            var newBadge = new AccountBadge
            {
                Type = "sponsor",
                Label = "Sponsor",
                Caption = $"Level {newLevel}",
                AccountId = subscription.AccountId.ToString()
            };

            // Add metadata
            newBadge.Meta.Add("level", new Value { StringValue = newLevel.ToString() });
            newBadge.Meta.Add("subscription_id", new Value { StringValue = subscription.Id.ToString() });

            // Grant the new badge using the AccountService
            var grantBadgeRequest = new GrantBadgeRequest
            {
                AccountId = subscription.AccountId.ToString(),
                Badge = newBadge
            };

            await accounts.GrantBadgeAsync(grantBadgeRequest);

            // Log the badge update
            logger.LogInformation("Sponsor badge updated for account {AccountId}: Level {Level}",
                subscription.AccountId, newLevel);
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the subscription creation
            logger.LogError(ex, "Failed to update sponsor badge for subscription {SubscriptionId}", subscription.Id);
        }
    }
}