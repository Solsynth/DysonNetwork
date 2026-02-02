using NodaTime;

namespace DysonNetwork.Wallet.Payment.PaymentHandlers;

public interface ISubscriptionOrder
{
    public string Id { get; }

    public string SubscriptionId { get; }

    public Instant BegunAt { get; }

    public Duration Duration { get; }

    public string Provider { get; }

    public string AccountId { get; }
}
