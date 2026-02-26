using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteSubscriptionService(DySubscriptionService.DySubscriptionServiceClient subscription)
{
    public async Task<DySubscription> GetSubscription(Guid accountId, string identifier)
    {
        var request = new DyGetSubscriptionRequest
        {
            AccountId = accountId.ToString(),
            Identifier = identifier
        };
        var response = await subscription.GetSubscriptionAsync(request);
        return response;
    }

    public async Task<DySubscription?> GetPerkSubscription(Guid accountId)
    {
        var request = new DyGetPerkSubscriptionRequest { AccountId = accountId.ToString() };
        var response = await subscription.GetPerkSubscriptionAsync(request);
        // Return null if subscription is empty (user has no active perk subscription)
        return string.IsNullOrEmpty(response.Id) ? null : response;
    }

    public async Task<List<DySubscription>> GetPerkSubscriptions(List<Guid> accountIds)
    {
        var request = new DyGetPerkSubscriptionsRequest();
        request.AccountIds.AddRange(accountIds.Select(id => id.ToString()));
        var response = await subscription.GetPerkSubscriptionsAsync(request);
        // Filter out empty subscriptions (users with no active perk subscription)
        return response.Subscriptions.Where(s => !string.IsNullOrEmpty(s.Id)).ToList();
    }

    public async Task<DySubscription> CreateSubscription(
        Guid accountId,
        string identifier,
        string paymentMethod,
        string? couponCode = null,
        bool isFreeTrial = false)
    {
        var request = new DyCreateSubscriptionRequest
        {
            AccountId = accountId.ToString(),
            Identifier = identifier,
            PaymentMethod = paymentMethod,
            IsFreeTrial = isFreeTrial
        };

        if (couponCode != null)
            request.CouponCode = couponCode;

        var response = await subscription.CreateSubscriptionAsync(request);
        return response;
    }

    public async Task<DySubscription> CancelSubscription(Guid accountId, string identifier)
    {
        var request = new DyCancelSubscriptionRequest
        {
            AccountId = accountId.ToString(),
            Identifier = identifier
        };
        var response = await subscription.CancelSubscriptionAsync(request);
        return response;
    }
}
