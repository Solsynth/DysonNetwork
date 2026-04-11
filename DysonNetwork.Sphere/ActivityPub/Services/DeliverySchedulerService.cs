using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class DeliveryBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InboxUri { get; set; } = null!;
    public string ActorUri { get; set; } = null!;
    public List<DeliveryItem> Items { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DeliveryItem
{
    public Guid DeliveryId { get; set; }
    public string ActivityId { get; set; } = null!;
    public string ActivityType { get; set; } = null!;
    public string ActivityPayload { get; set; } = null!;
    public int RetryCount { get; set; }
}

public class DeliveryDeadLetter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeliveryId { get; set; }
    public string InboxUri { get; set; } = null!;
    public string ActorUri { get; set; } = null!;
    public string ActivityType { get; set; } = null!;
    public string ActivityPayload { get; set; } = null!;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public Instant FailedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class DeliveryStatistics
{
    public int TotalDelivered { get; set; }
    public int TotalFailed { get; set; }
    public int PendingDelivery { get; set; }
    public int DeadLetterCount { get; set; }
    public double AverageDeliveryTimeMs { get; set; }
    public Dictionary<string, int> DeliveriesByType { get; set; } = [];
}

public class DeliveryMetricsService(AppDatabase db, ILogger<DeliveryMetricsService> logger)
{
    private readonly Dictionary<string, List<double>> _deliveryTimes = new();
    private readonly object _lock = new();

    public void RecordDeliveryTime(string activityType, double milliseconds)
    {
        lock (_lock)
        {
            if (!_deliveryTimes.ContainsKey(activityType))
                _deliveryTimes[activityType] = [];

            _deliveryTimes[activityType].Add(milliseconds);

            if (_deliveryTimes[activityType].Count > 1000)
                _deliveryTimes[activityType].RemoveAt(0);
        }
    }

    public async Task<DeliveryStatistics> GetStatisticsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var dayAgo = now - Duration.FromDays(1);

        var stats = new DeliveryStatistics
        {
            TotalDelivered = await db.ActivityPubDeliveries.CountAsync(d =>
                d.Status == DeliveryStatus.Sent && d.SentAt > dayAgo
            ),
            TotalFailed = await db.ActivityPubDeliveries.CountAsync(d =>
                d.Status == DeliveryStatus.Failed && d.CreatedAt > dayAgo
            ),
            PendingDelivery = await db.ActivityPubDeliveries.CountAsync(d =>
                d.Status == DeliveryStatus.Pending
            ),
            DeadLetterCount = await db.DeliveryDeadLetters.CountAsync(),
            DeliveriesByType = await db
                .ActivityPubDeliveries.Where(d => d.SentAt > dayAgo)
                .GroupBy(d => d.ActivityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count),
        };

        lock (_lock)
        {
            var allTimes = _deliveryTimes.Values.SelectMany(v => v).ToList();
            if (allTimes.Count > 0)
                stats.AverageDeliveryTimeMs = allTimes.Average();
        }

        return stats;
    }
}

public static class DeliveryRetryCalculator
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(12),
    };

    public static TimeSpan GetDelayForRetry(int retryCount)
    {
        if (retryCount < 0)
            return RetryDelays[0];

        if (retryCount >= RetryDelays.Length)
            return RetryDelays[^1];

        return RetryDelays[retryCount];
    }

    public static bool ShouldRetry(int retryCount, int maxRetries = 10)
    {
        return retryCount < maxRetries;
    }

    public static Instant? GetNextRetryTime(int retryCount)
    {
        if (!ShouldRetry(retryCount))
            return null;

        var delay = GetDelayForRetry(retryCount);
        return SystemClock.Instance.GetCurrentInstant() + Duration.FromTimeSpan(delay);
    }
}

public class DeliveryBatchService(AppDatabase db, ILogger<DeliveryBatchService> logger)
{
    private readonly Dictionary<string, DeliveryBatch> _pendingBatches = new();
    private readonly object _lock = new();
    private readonly TimeSpan _batchTimeout = TimeSpan.FromSeconds(5);

    public void AddToBatch(string inboxUri, string actorUri, DeliveryItem item)
    {
        var key = $"{inboxUri}|{actorUri}";

        lock (_lock)
        {
            if (!_pendingBatches.TryGetValue(key, out var batch))
            {
                batch = new DeliveryBatch { InboxUri = inboxUri, ActorUri = actorUri };
                _pendingBatches[key] = batch;
            }

            batch.Items.Add(item);
        }
    }

    public List<DeliveryBatch> GetReadyBatches()
    {
        var ready = new List<DeliveryBatch>();
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            var keysToRemove = new List<string>();

            foreach (var (key, batch) in _pendingBatches)
            {
                if (now - batch.CreatedAt >= _batchTimeout || batch.Items.Count >= 100)
                {
                    ready.Add(batch);
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
                _pendingBatches.Remove(key);
        }

        return ready;
    }

    public int GetPendingCount()
    {
        lock (_lock)
        {
            return _pendingBatches.Values.Sum(b => b.Items.Count);
        }
    }
}

