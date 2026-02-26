using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Wallet.Payment;

public class SubscriptionServiceGrpc(
    SubscriptionService subscriptionService,
    DyAccountService.DyAccountServiceClient accounts
) : DyDySubscriptionService.DySubscriptionServiceBase
{
    private readonly DyAccountService.DyAccountServiceClient _accounts = accounts;

    public override async Task<DySubscription> GetSubscription(
        DyGetSubscriptionRequest request,
        ServerCallContext context
    )
    {
        var subscription = await subscriptionService.GetSubscriptionAsync(
            Guid.Parse(request.AccountId),
            request.Identifier
        );
        return subscription?.ToProtoValue()
               ?? throw new RpcException(new Status(StatusCode.NotFound, "Subscription not found"));
    }

    public override async Task<DySubscription> GetPerkSubscription(
        DyGetPerkSubscriptionRequest request,
        ServerCallContext context
    )
    {
        var subscription = await subscriptionService.GetPerkSubscriptionAsync(
            Guid.Parse(request.AccountId)
        );
        // Return empty subscription if user has no active perk subscription (valid case)
        // RemoteSubscriptionService will convert empty subscription to null
        return subscription?.ToProtoValue() ?? new DySubscription { Id = "" };
    }

    public override async Task<DyGetPerkSubscriptionsResponse> GetPerkSubscriptions(
        DyGetPerkSubscriptionsRequest request,
        ServerCallContext context
    )
    {
        var accountIds = request.AccountIds.Select(Guid.Parse).ToList();
        var subscriptions = await subscriptionService.GetPerkSubscriptionsAsync(accountIds);

        var response = new DyGetPerkSubscriptionsResponse();
        foreach (var subscription in subscriptions.Values)
        {
            if (subscription != null)
            {
                response.Subscriptions.Add(subscription.ToProtoValue());
            }
        }

        return response;
    }

    public override async Task<DySubscription> CreateSubscription(
        DyCreateSubscriptionRequest request,
        ServerCallContext context
    )
    {
        var account = await _accounts.GetAccountAsync(new DyGetAccountRequest { Id = request.AccountId });

        if (account == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Account not found"));
        }

        var subscription = await subscriptionService.CreateSubscriptionAsync(
            account,
            request.Identifier,
            request.PaymentMethod,
            new SnPaymentDetails(),
            null,
            request.HasCouponCode ? request.CouponCode : null,
            request.IsFreeTrial,
            true
        );

        return subscription.ToProtoValue();
    }

    public override async Task<DySubscription> CancelSubscription(
        DyCancelSubscriptionRequest request,
        ServerCallContext context
    )
    {
        var subscription = await subscriptionService.CancelSubscriptionAsync(
            Guid.Parse(request.AccountId),
            request.Identifier
        );
        return subscription.ToProtoValue();
    }
}
