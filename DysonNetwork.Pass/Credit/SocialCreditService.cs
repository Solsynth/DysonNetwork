using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditService(AppDatabase db, ICacheService cache)
{
    private const string CacheKeyPrefix = "account:credits:";
    
    public async Task<SocialCreditRecord> AddRecord(string reasonType, string reason, double delta, Guid accountId)
    {
        var record = new SocialCreditRecord
        {
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta,
            AccountId = accountId,
        };
        db.SocialCreditRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }
    
    private const double BaseSocialCredit = 100;
    
    public async Task<double> GetSocialCredit(Guid accountId)
    {
        var cached = await cache.GetAsync<double?>($"{CacheKeyPrefix}{accountId}");
        if (cached.HasValue) return cached.Value;
        
        var records = await db.SocialCreditRecords
            .Where(x => x.AccountId == accountId)
            .SumAsync(x => x.Delta);
        records += BaseSocialCredit;
        await cache.SetAsync($"{CacheKeyPrefix}{accountId}", records);
        return records;
    }
}