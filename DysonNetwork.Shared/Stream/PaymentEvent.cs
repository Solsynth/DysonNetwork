namespace DysonNetwork.Shared.Stream;

public class PaymentOrderEvent
{
    public static string Type => "payments.orders";
    
    public Guid OrderId { get; set; }
    public Guid WalletId { get; set; }
    public Guid AccountId { get; set; }
    public string? AppIdentifier { get; set; }
    public string? ProductIdentifier { get; set; }
    public Dictionary<string, object> Meta { get; set; } = null!;
    public int Status { get; set; }
}