using System.Text.Json.Serialization;
using DysonNetwork.Shared.Queue;

namespace DysonNetwork.Passport.Startup;

public class PaymentOrderNameChangeCardEvent : PaymentOrderEventBase
{
    public PaymentOrderNameChangeCardMeta Meta { get; set; } = null!;
}

public class PaymentOrderNameChangeCardMeta
{
    [JsonPropertyName("purchase_id")] public Guid PurchaseId { get; set; }
}
