using System.Text.Json.Serialization;
using DysonNetwork.Shared.Queue;

namespace DysonNetwork.Passport.Startup;

public class PaymentOrderAffiliationPurchaseEvent : PaymentOrderEventBase
{
    public PaymentOrderAffiliationPurchaseMeta Meta { get; set; } = null!;
}

public class PaymentOrderAffiliationPurchaseMeta
{
    [JsonPropertyName("purchase_id")] public Guid PurchaseId { get; set; }
}
