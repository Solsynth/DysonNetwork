using Microsoft.EntityFrameworkCore;
using NodaTime;
using DysonNetwork.Shared.Models;

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
        
        var replicaData = await db.FileReplicas
            .Where(r => r.Status == SnFileReplicaStatus.Available)
            .Where(r => r.PoolId.HasValue)
            .Join(
                db.Files.Where(f => f.AccountId == accountId)
                    .Where(f => !f.IsMarkedRecycle)
                    .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now),
                r => r.ObjectId,
                f => f.Id,
                (r, f) => new { r.PoolId, r.ObjectId }
            )
            .Join(
                db.FileObjects,
                x => x.ObjectId,
                o => o.Id,
                (x, o) => new { x.PoolId, o.Size }
            )
            .ToListAsync();

        var poolUsages = replicaData
            .GroupBy(r => r.PoolId!.Value)
            .Select(g =>
            {
                var poolId = g.Key;
                var pool = db.Pools.Local.FirstOrDefault(p => p.Id == poolId) 
                           ?? db.Pools.Find(poolId);
                var multiplier = pool?.BillingConfig.CostMultiplier ?? 1.0;
                var totalBytes = g.Sum(x => x.Size);
                
                return new UsageDetails
                {
                    PoolId = poolId,
                    PoolName = pool?.Name ?? "Unknown",
                    UsageBytes = totalBytes,
                    Cost = totalBytes * multiplier / 1024.0 / 1024.0,
                    FileCount = g.Count()
                };
            })
            .ToList();

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
        
        var replicaData = await db.FileReplicas
            .Where(r => r.PoolId == poolId)
            .Where(r => r.Status == SnFileReplicaStatus.Available)
            .Join(
                db.Files.Where(f => f.AccountId == accountId)
                    .Where(f => !f.IsMarkedRecycle)
                    .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now),
                r => r.ObjectId,
                f => f.Id,
                (r, f) => r.ObjectId
            )
            .Distinct()
            .ToListAsync();

        var fileCount = replicaData.Count;
        
        var objectIds = replicaData.Distinct().ToList();
        var usageBytes = await db.FileObjects
            .Where(o => objectIds.Contains(o.Id))
            .SumAsync(o => o.Size);

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
        
        var billingData = await (from f in db.Files
            where f.AccountId == accountId
            where !f.IsMarkedRecycle
            where !f.ExpiredAt.HasValue || f.ExpiredAt > now
            from r in f.Object!.FileReplicas
            where r.Status == SnFileReplicaStatus.Available
            where r.PoolId.HasValue
            join p in db.Pools on r.PoolId equals p.Id
            join o in db.FileObjects on r.ObjectId equals o.Id
            select new
            {
                Size = o.Size,
                Multiplier = p.BillingConfig.CostMultiplier ?? 1.0
            }).ToListAsync();

        var totalCost = billingData.Sum(x => x.Size * x.Multiplier) / 1024.0 / 1024.0;

        return (long)Math.Ceiling(totalCost);
    }
}
