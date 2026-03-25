using System.Collections.Concurrent;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NATS.Client.Core;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubQueueService(
    INatsConnection nats,
    IClock clock,
    ILogger<ActivityPubQueueService> logger
) {
    public const int BatchSizeLimit = 50;
    public static readonly TimeSpan BatchTimeWindow = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<string, ActivityPubDeliveryBatch> _batches = new();
    private readonly object _cleanupLock = new();
    private Instant _lastCleanup = SystemClock.Instance.GetCurrentInstant();

    public async Task EnqueueDeliveryAsync(ActivityPubDeliveryMessage message)
    {
        var batchKey = GetBatchKey(message.InboxUri, message.ActorUri);
        
        var batch = _batches.GetOrAdd(batchKey, _ => new ActivityPubDeliveryBatch(
            message.InboxUri,
            message.ActorUri,
            clock
        ));

        var shouldFlush = batch.Add(message);

        if (shouldFlush)
        {
            await FlushBatchAsync(batchKey, batch);
        }
    }

    public async Task EnqueueBatchAsync(ActivityPubDeliveryBatchMessage batchMessage)
    {
        var rawMessage = InfraObjectCoder.ConvertObjectToByteString(batchMessage).ToByteArray();
        await nats.PublishAsync(ActivityPubDeliveryWorker.QueueName, rawMessage);
        logger.LogDebug("Enqueued batch of {Count} deliveries to {Inbox}", 
            batchMessage.Deliveries.Count, batchMessage.InboxUri);
    }

    private static string GetBatchKey(string inboxUri, string actorUri) => $"{inboxUri}|{actorUri}";

    private async Task FlushBatchAsync(string batchKey, ActivityPubDeliveryBatch batch)
    {
        if (_batches.TryRemove(batchKey, out var removedBatch))
        {
            var deliveries = removedBatch.GetDeliveries();
            if (deliveries.Count > 0)
            {
                var batchMessage = new ActivityPubDeliveryBatchMessage
                {
                    InboxUri = removedBatch.InboxUri,
                    ActorUri = removedBatch.ActorUri,
                    Deliveries = deliveries
                };
                await EnqueueBatchAsync(batchMessage);
            }
        }
    }

    public async Task FlushAllAsync()
    {
        var keys = _batches.Keys.ToList();
        foreach (var key in keys)
        {
            if (_batches.TryRemove(key, out var batch))
            {
                var deliveries = batch.GetDeliveries();
                if (deliveries.Count > 0)
                {
                    var batchMessage = new ActivityPubDeliveryBatchMessage
                    {
                        InboxUri = batch.InboxUri,
                        ActorUri = batch.ActorUri,
                        Deliveries = deliveries
                    };
                    await EnqueueBatchAsync(batchMessage);
                }
            }
        }
    }

    public async Task PeriodicFlushAsync()
    {
        var now = clock.GetCurrentInstant();
        
        foreach (var kvp in _batches)
        {
            var batch = kvp.Value;
            if (batch.ShouldFlush(now))
            {
                await FlushBatchAsync(kvp.Key, batch);
            }
        }
    }
}

public class ActivityPubDeliveryBatch
{
    private readonly List<ActivityPubDeliveryMessage> _messages = new();
    private readonly IClock _clock;
    private readonly object _lock = new();
    private Instant _lastAddTime;

    public string InboxUri { get; }
    public string ActorUri { get; }

    public ActivityPubDeliveryBatch(string inboxUri, string actorUri, IClock clock)
    {
        InboxUri = inboxUri;
        ActorUri = actorUri;
        _clock = clock;
        _lastAddTime = _clock.GetCurrentInstant();
    }

    public bool Add(ActivityPubDeliveryMessage message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            _lastAddTime = _clock.GetCurrentInstant();
            
            return _messages.Count >= ActivityPubQueueService.BatchSizeLimit;
        }
    }

    public bool ShouldFlush(Instant now)
    {
        lock (_lock)
        {
            return now - _lastAddTime > Duration.FromTimeSpan(ActivityPubQueueService.BatchTimeWindow);
        }
    }

    public List<ActivityPubDeliveryMessage> GetDeliveries()
    {
        lock (_lock)
        {
            var result = new List<ActivityPubDeliveryMessage>(_messages);
            _messages.Clear();
            return result;
        }
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

public class ActivityPubDeliveryBatchMessage
{
    public string InboxUri { get; set; } = string.Empty;
    public string ActorUri { get; set; } = string.Empty;
    public List<ActivityPubDeliveryMessage> Deliveries { get; set; } = [];
}
