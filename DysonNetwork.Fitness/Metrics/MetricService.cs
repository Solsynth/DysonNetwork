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
}
