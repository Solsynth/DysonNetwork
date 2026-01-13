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
        
        var poolUsages = await db.Pools
            .Select(p => new UsageDetails
            {
                PoolId = p.Id,
                PoolName = p.Name,
                UsageBytes = db.Files
                    .Where(f => f.AccountId == accountId)
                    .Where(f => !f.IsMarkedRecycle)
                    .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
                    .SelectMany(f => f.Object!.FileReplicas
                        .Where(r => r.PoolId == p.Id && r.Status == SnFileReplicaStatus.Available))
                    .Join(db.FileObjects,
                        r => r.ObjectId,
                        o => o.Id,
                        (r, o) => o.Size)
                    .DefaultIfEmpty(0L)
                    .Sum(),
                Cost = db.Files
                    .Where(f => f.AccountId == accountId)
                    .Where(f => !f.IsMarkedRecycle)
                    .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
                    .SelectMany(f => f.Object!.FileReplicas
                        .Where(r => r.PoolId == p.Id && r.Status == SnFileReplicaStatus.Available))
                    .Join(db.FileObjects,
                        r => r.ObjectId,
                        o => o.Id,
                        (r, o) => new { Size = o.Size, Multiplier = p.BillingConfig.CostMultiplier ?? 1.0 })
                    .Sum(x => x.Size * x.Multiplier) / 1024.0 / 1024.0,
                FileCount = db.Files
                    .Where(f => f.AccountId == accountId)
                    .Where(f => !f.IsMarkedRecycle)
                    .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
                    .SelectMany(f => f.Object!.FileReplicas
                        .Where(r => r.PoolId == p.Id && r.Status == SnFileReplicaStatus.Available))
                    .Count()
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
        var replicaQuery = db.Files
            .Where(f => f.AccountId == accountId)
            .Where(f => !f.IsMarkedRecycle)
            .Where(f => !f.ExpiredAt.HasValue || f.ExpiredAt > now)
            .SelectMany(f => f.Object!.FileReplicas
                .Where(r => r.PoolId == poolId && r.Status == SnFileReplicaStatus.Available));

        var usageBytes = await replicaQuery
            .Join(db.FileObjects,
                r => r.ObjectId,
                o => o.Id,
                (r, o) => o.Size)
            .DefaultIfEmpty(0L)
            .SumAsync();

        var fileCount = await replicaQuery.CountAsync();

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
