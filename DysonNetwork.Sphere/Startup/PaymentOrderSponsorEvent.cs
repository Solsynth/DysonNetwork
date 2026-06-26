using System.Text.Json.Serialization;
using DysonNetwork.Shared.Queue;

namespace DysonNetwork.Sphere.Startup;

public class PaymentOrderSponsorEvent : PaymentOrderEventBase
{
    public PaymentOrderSponsorMeta Meta { get; set; } = null!;
}

public class PaymentOrderSponsorMeta
{
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("post_id")] public Guid PostId { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; } = null!;
}
