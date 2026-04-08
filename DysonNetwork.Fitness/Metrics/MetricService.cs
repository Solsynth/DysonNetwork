using DysonNetwork.Fitness.Metrics;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Fitness.Metrics;

public class MetricService(AppDatabase db, ILogger<MetricService> logger)
{
    public async Task<SnFitnessMetric?> GetMetricByIdAsync(Guid id)
    {
        return await db.FitnessMetrics
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
    }

    public async Task<IEnumerable<SnFitnessMetric>> GetMetricsByAccountAsync(Guid accountId, FitnessMetricType? type = null, int skip = 0, int take = 50)
    {
        var query = db.FitnessMetrics
            .Where(m => m.AccountId == accountId && m.DeletedAt == null)
            .AsQueryable();

        if (type.HasValue)
            query = query.Where(m => m.MetricType == type.Value);

        return await query
            .OrderByDescending(m => m.RecordedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<SnFitnessMetric> CreateMetricAsync(SnFitnessMetric metric)
    {
        db.FitnessMetrics.Add(metric);
        await db.SaveChangesAsync();
        logger.LogInformation("Created metric {MetricId} of type {MetricType} for account {AccountId}", 
            metric.Id, metric.MetricType, metric.AccountId);
        return metric;
    }

    public async Task<SnFitnessMetric?> UpdateMetricAsync(Guid id, SnFitnessMetric updated)
    {
        var metric = await db.FitnessMetrics.FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
        if (metric is null) return null;

        metric.MetricType = updated.MetricType;
        metric.Value = updated.Value;
        metric.Unit = updated.Unit;
        metric.RecordedAt = updated.RecordedAt;
        metric.Notes = updated.Notes;
        metric.Source = updated.Source;
        metric.Visibility = updated.Visibility;
        metric.UpdatedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);

        await db.SaveChangesAsync();
        logger.LogInformation("Updated metric {MetricId}", id);
        return metric;
    }

    public async Task<bool> DeleteMetricAsync(Guid id)
    {
        var metric = await db.FitnessMetrics.FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
        if (metric is null) return false;

        metric.DeletedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        logger.LogInformation("Soft deleted metric {MetricId}", id);
        return true;
    }

    public async Task<Dictionary<FitnessMetricType, SnFitnessMetric>> GetLatestMetricsByTypeAsync(Guid accountId)
    {
        var latestMetrics = await db.FitnessMetrics
            .Where(m => m.AccountId == accountId && m.DeletedAt == null)
            .GroupBy(m => m.MetricType)
            .Select(g => g.OrderByDescending(m => m.RecordedAt).First())
            .ToDictionaryAsync(m => m.MetricType);

        return latestMetrics;
    }

    public async Task<List<SnFitnessMetric>> CreateMetricsBatchAsync(IEnumerable<SnFitnessMetric> metrics)
    {
        var now = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        var metricList = metrics.Select(m =>
        {
            m.Id = Guid.NewGuid();
            m.CreatedAt = now;
            m.UpdatedAt = now;
            return m;
        }).ToList();

        var externalIds = metricList
            .Where(m => !string.IsNullOrEmpty(m.ExternalId))
            .Select(m => m.ExternalId!)
            .ToList();

        var existingByExternalId = new Dictionary<string, SnFitnessMetric>(StringComparer.OrdinalIgnoreCase);
        if (externalIds.Any())
        {
            var existing = await db.FitnessMetrics
                .Where(m => externalIds.Contains(m.ExternalId!))
                .ToListAsync();
            foreach (var e in existing)
            {
                if (e.ExternalId != null)
                    existingByExternalId[e.ExternalId] = e;
            }
        }

        var toInsert = new List<SnFitnessMetric>();
        var toUpdate = new List<SnFitnessMetric>();

        foreach (var metric in metricList)
        {
            if (!string.IsNullOrEmpty(metric.ExternalId) && existingByExternalId.TryGetValue(metric.ExternalId, out var existing))
            {
                existing.MetricType = metric.MetricType;
                existing.Value = metric.Value;
                existing.Unit = metric.Unit;
                existing.RecordedAt = metric.RecordedAt;
                existing.Notes = metric.Notes;
                existing.Source = metric.Source;
                existing.Visibility = metric.Visibility;
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                toInsert.Add(metric);
            }
        }

        var accountId = toInsert.FirstOrDefault()?.AccountId ?? toUpdate.FirstOrDefault()?.AccountId;

        if (toInsert.Any())
        {
            db.FitnessMetrics.AddRange(toInsert);
        }

        if (toUpdate.Any())
        {
            foreach (var metric in toUpdate)
            {
                db.FitnessMetrics.Update(metric);
            }
        }

        if (toInsert.Any() || toUpdate.Any())
        {
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Created {CreatedCount} metrics, updated {UpdatedCount} metrics in batch for account {AccountId}", 
            toInsert.Count, toUpdate.Count, accountId);
        return toInsert.Concat(toUpdate).ToList();
    }

    public async Task<int> UpdateMetricsVisibilityAsync(Guid accountId, IEnumerable<Guid> metricIds, FitnessVisibility visibility)
    {
        var ids = metricIds.ToList();
        var count = await db.FitnessMetrics
            .Where(m => m.AccountId == accountId && ids.Contains(m.Id) && m.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Visibility, visibility)
                .SetProperty(m => m.UpdatedAt, NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)));

        logger.LogInformation("Updated visibility to {Visibility} for {Count} metrics for account {AccountId}",
            visibility, count, accountId);
        return count;
    }
}
