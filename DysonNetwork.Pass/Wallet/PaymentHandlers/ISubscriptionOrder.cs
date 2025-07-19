using NodaTime;

namespace DysonNetwork.Pass.Wallet.PaymentHandlers;

public interface ISubscriptionOrder
{
    public string Id { get; }

    public string SubscriptionId { get; }

    public Instant BegunAt { get; }

    public Duration Duration { get; }

    public string Provider { get; }

    public string AccountId { get; }
}
