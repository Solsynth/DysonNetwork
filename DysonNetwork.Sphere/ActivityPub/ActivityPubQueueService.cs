using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NATS.Client.Core;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubQueueService(INatsConnection nats)
{
    public async Task EnqueueDeliveryAsync(ActivityPubDeliveryMessage message)
    {
        var rawMessage = InfraObjectCoder.ConvertObjectToByteString(message).ToByteArray();
        await nats.PublishAsync(ActivityPubDeliveryWorker.QueueName, rawMessage);
    }
}

public class ActivityPubDeliveryMessage
{
    public Guid DeliveryId { get; set; }
    public string ActivityId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public Dictionary<string, object> Activity { get; set; } = [];
    public string ActorUri { get; set; } = string.Empty;
    public string InboxUri { get; set; } = string.Empty;
    public int CurrentRetry { get; set; } = 0;
}
