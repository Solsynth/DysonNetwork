using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Queue;
using LiveStreamAwardAttitude = DysonNetwork.Shared.Models.LiveStreamAwardAttitude;

namespace DysonNetwork.Sphere.Startup;

public class PaymentOrderAwardEvent : PaymentOrderEventBase
{
    public PaymentOrderAwardMeta Meta { get; set; } = null!;
}

public class PaymentOrderAwardMeta
{
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("post_id")] public Guid PostId { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; } = null!;
    [JsonPropertyName("attitude")] public Shared.Models.PostReactionAttitude Attitude { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public class PaymentOrderLiveStreamAwardEvent : PaymentOrderEventBase
{
    public PaymentOrderLiveStreamAwardMeta Meta { get; set; } = null!;
}

public class PaymentOrderLiveStreamAwardMeta
{
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("livestream_id")] public Guid LiveStreamId { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; } = null!;
    [JsonPropertyName("attitude")] public LiveStreamAwardAttitude Attitude { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
