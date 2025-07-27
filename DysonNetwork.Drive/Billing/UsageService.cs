using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Drive.Billing;

public class UsageDetails
{
    public required Guid PoolId { get; set; }
    public required string PoolName { get; set; }
    public required long UsageBytes { get; set; }
    public required double Cost { get; set; }
    public required long FileCount { get; set; }
}

public class UsageDetailsWithPercentage : UsageDetails
{
    public required double Percentage { get; set; }
}

public class TotalUsageDetails
{
    public required List<UsageDetailsWithPercentage> PoolUsages { get; set; }
    public required long TotalUsageBytes { get; set; }
    public required double TotalCost { get; set; }
    public required long TotalFileCount { get; set; }
}

public class UsageService(AppDatabase db)
{
    public async Task<TotalUsageDetails> GetTotalUsage()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var fileQuery = db.Files
            .Where(f => !f.IsMarkedRecycle)
            .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
            .AsQueryable();
        
        var poolUsages = await db.Pools
            .Select(p => new UsageDetails
            {
                PoolId = p.Id,
                PoolName = p.Name,
                UsageBytes = fileQuery
                    .Where(f => f.PoolId == p.Id)
                    .Sum(f => f.Size),
                Cost = fileQuery
                           .Where(f => f.PoolId == p.Id)
                           .Sum(f => f.Size) / 1024.0 / 1024.0 *
                       (p.BillingConfig.CostMultiplier ?? 1.0),
                FileCount = db.Files
                    .Count(f => f.PoolId == p.Id)
            })
            .ToListAsync();

        var totalUsage = poolUsages.Sum(p => p.UsageBytes);
        var totalCost = poolUsages.Sum(p => p.Cost);
        var totalFileCount = poolUsages.Sum(p => p.FileCount);

        var poolUsagesWithPercentage = poolUsages.Select(p => new UsageDetailsWithPercentage
        {
            PoolId = p.PoolId,
            PoolName = p.PoolName,
            UsageBytes = p.UsageBytes,
            Cost = p.Cost,
            FileCount = p.FileCount,
            Percentage = totalUsage > 0 ? (double)p.UsageBytes / totalUsage : 0
        }).ToList();

        return new TotalUsageDetails
        {
            PoolUsages = poolUsagesWithPercentage,
            TotalUsageBytes = totalUsage,
            TotalCost = totalCost,
            TotalFileCount = totalFileCount
        };
    }

    public async Task<UsageDetails?> GetPoolUsage(Guid poolId)
    {
        var pool = await db.Pools.FindAsync(poolId);
        if (pool == null)
        {
            return null;
        }
        
        var now = SystemClock.Instance.GetCurrentInstant();
        var fileQuery = db.Files
            .Where(f => !f.IsMarkedRecycle)
            .Where(f => f.ExpiredAt.HasValue && f.ExpiredAt > now)
            .AsQueryable();

        var usageBytes = await fileQuery
            .SumAsync(f => f.Size);

        var fileCount = await fileQuery
            .CountAsync();

        var cost = usageBytes / 1024.0 / 1024.0 *
                   (pool.BillingConfig.CostMultiplier ?? 1.0);

        return new UsageDetails
        {
            PoolId = pool.Id,
            PoolName = pool.Name,
            UsageBytes = usageBytes,
            Cost = cost,
            FileCount = fileCount
        };
    }
}