using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteSubscriptionService(SubscriptionService.SubscriptionServiceClient subscription)
{
    public async Task<Subscription> GetSubscription(Guid accountId, string identifier)
    {
        var request = new GetSubscriptionRequest
        {
            AccountId = accountId.ToString(),
            Identifier = identifier
        };
        var response = await subscription.GetSubscriptionAsync(request);
        return response;
    }

    public async Task<Subscription> GetPerkSubscription(Guid accountId)
    {
        var request = new GetPerkSubscriptionRequest { AccountId = accountId.ToString() };
        var response = await subscription.GetPerkSubscriptionAsync(request);
        return response;
    }

    public async Task<List<Subscription>> GetPerkSubscriptions(List<Guid> accountIds)
    {
        var request = new GetPerkSubscriptionsRequest();
        request.AccountIds.AddRange(accountIds.Select(id => id.ToString()));
        var response = await subscription.GetPerkSubscriptionsAsync(request);
        return response.Subscriptions.ToList();
    }

    public async Task<Subscription> CreateSubscription(
        Guid accountId,
        string identifier,
        string paymentMethod,
        string? couponCode = null,
        bool isFreeTrial = false)
    {
        var request = new CreateSubscriptionRequest
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

    public async Task<Subscription> CancelSubscription(Guid accountId, string identifier)
    {
        var request = new CancelSubscriptionRequest
        {
            AccountId = accountId.ToString(),
            Identifier = identifier
        };
        var response = await subscription.CancelSubscriptionAsync(request);
        return response;
    }
}
