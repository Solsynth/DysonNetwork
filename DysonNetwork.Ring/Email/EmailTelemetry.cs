using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DysonNetwork.Ring.Email;

public static class EmailTelemetry
{
    public const string ActivitySourceName = "DysonNetwork.Ring.Email";
    public const string MeterName = "DysonNetwork.Ring.Email";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> EnqueueRequests = Meter.CreateCounter<long>("ring.emails.enqueue_requests");
    public static readonly Counter<long> DeliveryAttempts = Meter.CreateCounter<long>("ring.emails.delivery_attempts");
    public static readonly Counter<long> DeliveryResults = Meter.CreateCounter<long>("ring.emails.delivery_results");
    public static readonly Histogram<double> DeliveryDuration = Meter.CreateHistogram<double>("ring.emails.delivery_duration_ms");
}
