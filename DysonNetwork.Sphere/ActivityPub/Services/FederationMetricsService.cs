using System.Collections.Concurrent;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class FederationMetrics
{
    public long TotalFetches;
    public long SuccessfulFetches;
    public long FailedFetches;
    public long RedirectsHandled;
    public long DeliveriesSent;
    public long DeliveriesSucceeded;
    public long DeliveriesFailed;
    public long SignaturesVerified;
    public long SignaturesFailed;
    public long OutboxBackfills;
    public long PostsFetched;

    public ConcurrentDictionary<string, InstanceMetrics> InstanceMetrics = new();
}

public class InstanceMetrics
{
    public string Domain = string.Empty;
    public long Fetches;
    public long Failures;
    public long LastFetchAt;
    public Instant? LastFailureAt;
}

public class FederationMetricsService : IFederationMetricsService
{
    private readonly FederationMetrics _metrics = new();
    private readonly ConcurrentDictionary<string, long> _instanceFetchCounts = new();
    private readonly ConcurrentDictionary<string, long> _instanceFailureCounts = new();

    public FederationMetrics GetMetrics() => _metrics;

    public void RecordFetch(bool success, string? domain = null, bool isRedirect = false)
    {
        Interlocked.Increment(ref _metrics.TotalFetches);

        if (isRedirect)
        {
            Interlocked.Increment(ref _metrics.RedirectsHandled);
        }
        else if (success)
        {
            Interlocked.Increment(ref _metrics.SuccessfulFetches);
            if (domain != null)
            {
                _instanceFetchCounts.AddOrUpdate(domain, 1, (_, c) => c + 1);
            }
        }
        else
        {
            Interlocked.Increment(ref _metrics.FailedFetches);
            if (domain != null)
            {
                _instanceFailureCounts.AddOrUpdate(domain, 1, (_, c) => c + 1);
            }
        }

        UpdateInstanceMetrics(domain, success);
    }

    public void RecordDelivery(bool success)
    {
        Interlocked.Increment(ref _metrics.DeliveriesSent);

        if (success)
        {
            Interlocked.Increment(ref _metrics.DeliveriesSucceeded);
        }
        else
        {
            Interlocked.Increment(ref _metrics.DeliveriesFailed);
        }
    }

    public void RecordSignatureVerification(bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _metrics.SignaturesVerified);
        }
        else
        {
            Interlocked.Increment(ref _metrics.SignaturesFailed);
        }
    }

    public void RecordOutboxBackfill(int postsFetched)
    {
        Interlocked.Increment(ref _metrics.OutboxBackfills);
        Interlocked.Add(ref _metrics.PostsFetched, postsFetched);
    }

    public void RecordPostsFetched(int count)
    {
        Interlocked.Add(ref _metrics.PostsFetched, count);
    }

    private void UpdateInstanceMetrics(string? domain, bool success)
    {
        if (string.IsNullOrEmpty(domain)) return;

        if (_metrics.InstanceMetrics.TryGetValue(domain, out var existing))
        {
            existing.Fetches++;
            if (!success) existing.Failures++;
            existing.LastFetchAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            _metrics.InstanceMetrics.TryAdd(domain, new InstanceMetrics
            {
                Domain = domain,
                Fetches = 1,
                Failures = success ? 0 : 1,
                LastFetchAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }
    }

    public InstanceMetrics? GetInstanceMetrics(string domain)
    {
        return _metrics.InstanceMetrics.TryGetValue(domain, out var metrics) ? metrics : null;
    }

    public IEnumerable<InstanceMetrics> GetAllInstanceMetrics()
    {
        return _metrics.InstanceMetrics.Values.OrderByDescending(m => m.Fetches);
    }
}

public interface IFederationMetricsService
{
    FederationMetrics GetMetrics();
    void RecordFetch(bool success, string? domain = null, bool isRedirect = false);
    void RecordDelivery(bool success);
    void RecordSignatureVerification(bool success);
    void RecordOutboxBackfill(int postsFetched);
    void RecordPostsFetched(int count);
    InstanceMetrics? GetInstanceMetrics(string domain);
    IEnumerable<InstanceMetrics> GetAllInstanceMetrics();
}
