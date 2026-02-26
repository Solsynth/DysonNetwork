using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Drive.Billing;

public class QuotaService(
    AppDatabase db,
    UsageService usage,
    DyAccountService.DyAccountServiceClient accounts,
    ICacheService cache
)
{
    public async Task<(bool ok, long billable, long quota)> IsFileAcceptable(Guid accountId, double costMultiplier, long newFileSize)
    {
        // The billable unit is MiB
        var billableUnit = (long)Math.Ceiling(newFileSize / 1024.0 / 1024.0 * costMultiplier);
        var totalBillableUsage = await usage.GetTotalBillableUsage(accountId);
        var quota = await GetQuota(accountId);
        return (totalBillableUsage + billableUnit <= quota, billableUnit, quota);
    }

    public async Task<long> GetQuota(Guid accountId)
    {
        var cacheKey = $"file:quota:{accountId}";
        var cachedResult = await cache.GetAsync<long?>(cacheKey);
        if (cachedResult.HasValue) return cachedResult.Value;
        
        var (based, extra) = await GetQuotaVerbose(accountId);
        var quota = based + extra;
        await cache.SetAsync(cacheKey, quota, expiry: TimeSpan.FromMinutes(30));
        return quota;
    }
    
    public async Task<(long based, long extra)> GetQuotaVerbose(Guid accountId)
    {
        

        var response = await accounts.GetAccountAsync(new DyGetAccountRequest { Id = accountId.ToString() });
        var perkSubscription = response.PerkSubscription;

        // The base quota is 1GiB, T1 is 5GiB, T2 is 10GiB, T3 is 15GiB
        var basedQuota = 1L;
        if (perkSubscription != null)
        {
            var privilege = PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(perkSubscription.Identifier);
            basedQuota = privilege switch
            {
                1 => 5L,
                2 => 10L,
                3 => 15L,
                _ => basedQuota
            };
        }
        
        // The based quota is in GiB, we need to convert it to MiB
        basedQuota *= 1024L;
        
        var now = SystemClock.Instance.GetCurrentInstant();
        var extraQuota = await db.QuotaRecords
            .Where(e => e.AccountId == accountId)
            .Where(e => !e.ExpiredAt.HasValue || e.ExpiredAt > now)
            .SumAsync(e => e.Quota);
        
        return (basedQuota, extraQuota);
    }
}