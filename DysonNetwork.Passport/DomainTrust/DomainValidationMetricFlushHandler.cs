using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Passport.DomainTrust;

public class DomainValidationMetricInfo
{
    public string Domain { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public bool IsVerified { get; set; }
    public Instant CheckedAt { get; set; }
}

public class DomainValidationMetricFlushHandler(
    IServiceProvider serviceProvider,
    ILogger<DomainValidationMetricFlushHandler> logger
) : IFlushHandler<DomainValidationMetricInfo>
{
    public async Task FlushAsync(IReadOnlyList<DomainValidationMetricInfo> items)
    {
        logger.LogInformation("Flushing {Count} domain validation metrics...", items.Count);

        var grouped = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain))
            .GroupBy(x => x.Domain)
            .Select(g => new
            {
                Domain = g.Key,
                CheckCount = g.Count(),
                BlockedCount = g.Count(x => !x.IsAllowed),
                VerifiedCount = g.Count(x => x.IsVerified),
                LastCheckedAt = g.Max(x => x.CheckedAt)
            })
            .ToList();

        if (grouped.Count == 0)
            return;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var domains = grouped.Select(x => x.Domain).ToList();
        var existing = await db.DomainValidationMetrics
            .Where(x => domains.Contains(x.Domain))
            .ToDictionaryAsync(x => x.Domain);

        foreach (var item in grouped)
        {
            if (existing.TryGetValue(item.Domain, out var metric))
            {
                metric.CheckCount += item.CheckCount;
                metric.BlockedCount += item.BlockedCount;
                metric.VerifiedCount += item.VerifiedCount;
                metric.LastCheckedAt = item.LastCheckedAt;
            }
            else
            {
                db.DomainValidationMetrics.Add(new SnDomainValidationMetric
                {
                    Domain = item.Domain,
                    CheckCount = item.CheckCount,
                    BlockedCount = item.BlockedCount,
                    VerifiedCount = item.VerifiedCount,
                    LastCheckedAt = item.LastCheckedAt
                });
            }
        }

        await db.SaveChangesAsync();
    }
}

public class DomainValidationMetricFlushJob(
    FlushBufferService flushBuffer,
    DomainValidationMetricFlushHandler handler,
    ILogger<DomainValidationMetricFlushJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            logger.LogInformation("Running domain validation metric flush job...");
            await flushBuffer.FlushAsync(handler);
            logger.LogInformation("Completed domain validation metric flush job...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running domain validation metric flush job...");
        }
    }
}
