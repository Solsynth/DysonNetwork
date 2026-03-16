using System.Text.Json.Serialization;
using DysonNetwork.Shared.Queue;

namespace DysonNetwork.Passport.Startup;

public class PaymentOrderRealmBoostEvent : PaymentOrderEventBase
{
    public PaymentOrderRealmBoostMeta Meta { get; set; } = null!;
}

public class PaymentOrderRealmBoostMeta
{
    [JsonPropertyName("realm_id")] public Guid RealmId { get; set; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("shares")] public int Shares { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = null!;
    [JsonPropertyName("amount")] public string Amount { get; set; } = null!;
}
