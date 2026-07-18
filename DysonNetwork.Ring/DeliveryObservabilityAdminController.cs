using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring;

[ApiController]
[Route("/api/admin/delivery-observability")]
[Authorize]
[ApiFeature("admin.delivery-observability", Revision = 1)]
public class DeliveryObservabilityAdminController(AppDatabase db, IClock clock) : ControllerBase
{
    public class DeliverySummary
    {
        public long Total { get; init; }
        public long Successful { get; init; }
        public long Failed { get; init; }
        public long InvalidToken { get; init; }
        public long Skipped { get; init; }
        public double? SuccessRate { get; init; }
    }

    public sealed class DeliveryBreakdown : DeliverySummary
    {
        public string Key { get; init; } = string.Empty;
    }

    public sealed class EmailDeliveryOverview
    {
        public DeliverySummary Summary { get; init; } = new();
        public List<DeliveryBreakdown> BySource { get; init; } = [];
    }

    public sealed class NotificationDeliveryOverview
    {
        public long SendRequests { get; init; }
        public List<DeliveryBreakdown> SendRequestsByTopic { get; init; } = [];
        public DeliverySummary Summary { get; init; } = new();
        public List<DeliveryBreakdown> ByProvider { get; init; } = [];
        public List<DeliveryBreakdown> ByTopic { get; init; } = [];
    }

    [HttpGet("emails")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailDeliveryOverview>> GetEmailDeliveryOverview(
        [FromQuery] Instant? from = null,
        [FromQuery] Instant? to = null,
        CancellationToken cancellationToken = default
    )
    {
        var (rangeStart, rangeEnd) = ResolveRange(from, to);
        var records = db.EmailDeliveryRecords
            .AsNoTracking()
            .Where(r => r.CreatedAt >= rangeStart && r.CreatedAt <= rangeEnd);

        return Ok(new EmailDeliveryOverview
        {
            Summary = await BuildSummaryAsync(records, cancellationToken),
            BySource = await BuildBreakdownAsync(records, nameof(SnEmailDeliveryRecord.Source), cancellationToken)
        });
    }

    [HttpGet("notifications")]
    [AskPermission("notifications.send")]
    public async Task<ActionResult<NotificationDeliveryOverview>> GetNotificationDeliveryOverview(
        [FromQuery] Instant? from = null,
        [FromQuery] Instant? to = null,
        CancellationToken cancellationToken = default
    )
    {
        var (rangeStart, rangeEnd) = ResolveRange(from, to);
        var records = db.NotificationDeliveryRecords
            .AsNoTracking()
            .Where(r => r.CreatedAt >= rangeStart && r.CreatedAt <= rangeEnd);

        var sendRecords = db.NotificationSendRecords
            .AsNoTracking()
            .Where(r => r.CreatedAt >= rangeStart && r.CreatedAt <= rangeEnd);

        return Ok(new NotificationDeliveryOverview
        {
            SendRequests = await sendRecords.LongCountAsync(cancellationToken),
            SendRequestsByTopic = await BuildSendBreakdownAsync(sendRecords, cancellationToken),
            Summary = await BuildSummaryAsync(records, cancellationToken),
            ByProvider = await BuildBreakdownAsync(records, nameof(SnNotificationDeliveryRecord.Provider), cancellationToken),
            ByTopic = await BuildBreakdownAsync(records, nameof(SnNotificationDeliveryRecord.Topic), cancellationToken)
        });
    }

    private (Instant Start, Instant End) ResolveRange(Instant? from, Instant? to)
    {
        var end = to ?? clock.GetCurrentInstant();
        var start = from ?? end - Duration.FromDays(30);
        if (start > end)
            throw new ArgumentException("from must be earlier than to.");
        return (start, end);
    }

    private static async Task<DeliverySummary> BuildSummaryAsync<T>(
        IQueryable<T> records,
        CancellationToken cancellationToken
    ) where T : class
    {
        var outcomes = await records
            .Select(r => EF.Property<DeliveryOutcome>(r, nameof(SnEmailDeliveryRecord.Outcome)))
            .GroupBy(outcome => outcome)
            .Select(group => new { Outcome = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return ToSummary(outcomes.Select(o => (o.Outcome, o.Count)));
    }

    private static async Task<List<DeliveryBreakdown>> BuildBreakdownAsync<T>(
        IQueryable<T> records,
        string keyProperty,
        CancellationToken cancellationToken
    ) where T : class
    {
        var rows = await records
            .GroupBy(r => new
            {
                Key = EF.Property<string>(r, keyProperty),
                Outcome = EF.Property<DeliveryOutcome>(r, nameof(SnEmailDeliveryRecord.Outcome))
            })
            .Select(group => new
            {
                group.Key.Key,
                group.Key.Outcome,
                Count = group.LongCount()
            })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(row => row.Key).Select(group =>
        {
            var summary = ToSummary(group.Select(row => (row.Outcome, row.Count)));
            return new DeliveryBreakdown
            {
                Key = group.Key,
                Total = summary.Total,
                Successful = summary.Successful,
                Failed = summary.Failed,
                InvalidToken = summary.InvalidToken,
                Skipped = summary.Skipped,
                SuccessRate = summary.SuccessRate
            };
        }).OrderByDescending(row => row.Total).ToList();
    }

    private static async Task<List<DeliveryBreakdown>> BuildSendBreakdownAsync(
        IQueryable<SnNotificationSendRecord> records,
        CancellationToken cancellationToken
    )
    {
        var rows = await records
            .GroupBy(r => r.Topic)
            .Select(group => new { Key = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new DeliveryBreakdown { Key = row.Key, Total = row.Count })
            .OrderByDescending(row => row.Total)
            .ToList();
    }

    private static DeliverySummary ToSummary(IEnumerable<(DeliveryOutcome Outcome, long Count)> outcomes)
    {
        var values = outcomes.ToDictionary(value => value.Outcome, value => value.Count);
        var successful = values.GetValueOrDefault(DeliveryOutcome.Success);
        var failed = values.GetValueOrDefault(DeliveryOutcome.Failure);
        var invalidToken = values.GetValueOrDefault(DeliveryOutcome.InvalidToken);
        var skipped = values.GetValueOrDefault(DeliveryOutcome.Skipped);
        var attempts = successful + failed + invalidToken;
        return new DeliverySummary
        {
            Total = values.Values.Sum(),
            Successful = successful,
            Failed = failed,
            InvalidToken = invalidToken,
            Skipped = skipped,
            SuccessRate = attempts == 0 ? null : (double)successful / attempts
        };
    }
}
