using DysonNetwork.Shared.EventBus;

namespace DysonNetwork.Shared.Queue;

public class PaymentOrderEvent : PaymentOrderEventBase
{
    public Dictionary<string, object> Meta { get; set; } = null!;
}

public class PaymentOrderEventBase : EventBase
{
    public static string Type => "payment_orders";
    public override string EventType => Type;
    
    public Guid OrderId { get; set; }
    public Guid WalletId { get; set; }
    public Guid AccountId { get; set; }
    public string? AppIdentifier { get; set; }
    public string? ProductIdentifier { get; set; }
    public int Status { get; set; }
}
