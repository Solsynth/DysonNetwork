namespace DysonNetwork.Shared.Stream;

public class PaymentOrderEvent : PaymentOrderEventBase
{
    public Dictionary<string, object> Meta { get; set; } = null!;
}

public class PaymentOrderEventBase
{
    public static string Type => "payment_orders";
    
    public Guid OrderId { get; set; }
    public Guid WalletId { get; set; }
    public Guid AccountId { get; set; }
    public string? AppIdentifier { get; set; }
    public string? ProductIdentifier { get; set; }
    public int Status { get; set; }
}