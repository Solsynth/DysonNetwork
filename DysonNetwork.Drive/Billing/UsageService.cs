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

public class TotalUsageDetails
{
    public required List<UsageDetails> PoolUsages { get; set; }
    public required long TotalUsageBytes { get; set; }
    public required long TotalFileCount { get; set; }
    
    // Quota, cannot be loaded in the service, cause circular dependency
    // Let the controller do the calculation
    public long? TotalQuota { get; set; }
    public long? UsedQuota { get; set; }
}

public class UsageService(AppDatabase db)
{
    public async Task<TotalUsageDetails> GetTotalUsage(Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var fileQuery = db.Files
            .Where(f => !f.IsMarkedRecycle)
            .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
            .Where(f => f.AccountId == accountId)
            .AsQueryable();
        
        var poolUsages = await db.Pools
            .Select(p => new UsageDetails
            {
                PoolId = p.Id,
                PoolName = p.Name,
                UsageBytes = fileQuery
                    .Where(f => f.PoolId == p.Id)
                    .Include(f => f.Object)
                    .Sum(f => f.Size),
                Cost = fileQuery
                           .Where(f => f.PoolId == p.Id)
                           .Include(f => f.Object)
                           .Sum(f => f.Size) / 1024.0 / 1024.0 *
                       (p.BillingConfig.CostMultiplier ?? 1.0),
                FileCount = fileQuery
                    .Count(f => f.PoolId == p.Id)
            })
            .ToListAsync();

        var totalUsage = poolUsages.Sum(p => p.UsageBytes);
        var totalFileCount = poolUsages.Sum(p => p.FileCount);

        return new TotalUsageDetails
        {
            PoolUsages = poolUsages,
            TotalUsageBytes = totalUsage,
            TotalFileCount = totalFileCount,
            UsedQuota = await GetTotalBillableUsage(accountId)
        };
    }

    public async Task<UsageDetails?> GetPoolUsage(Guid poolId, Guid accountId)
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
            .Where(f => f.AccountId == accountId)
            .AsQueryable();

        var usageBytes = await fileQuery
            .Include(f => f.Object)
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

    public async Task<long> GetTotalBillableUsage(Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var files = await db.Files
            .Where(f => f.AccountId == accountId)
            .Where(f => f.PoolId.HasValue)
            .Where(f => !f.IsMarkedRecycle)
            .Include(f => f.Pool)
            .Include(f => f.Object)
            .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
            .Select(f => new
            {
                f.Size,
                Multiplier = f.Pool!.BillingConfig.CostMultiplier ?? 1.0
            })
            .ToListAsync();

        var totalCost = files.Sum(f => f.Size * f.Multiplier) / 1024.0 / 1024.0;

        return (long)Math.Ceiling(totalCost);
    }
}