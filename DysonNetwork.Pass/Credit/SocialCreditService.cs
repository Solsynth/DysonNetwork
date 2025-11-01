using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditService(AppDatabase db, ICacheService cache)
{
    private const string CacheKeyPrefix = "account:credits:";
    
    public async Task<SnSocialCreditRecord> AddRecord(string reasonType, string reason, double delta, Guid accountId)
    {
        var record = new SnSocialCreditRecord
        {
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta,
            AccountId = accountId,
        };
        db.SocialCreditRecords.Add(record);
        await db.SaveChangesAsync();
        
        await db.AccountProfiles
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(p => p.SetProperty(v => v.SocialCredits, v => v.SocialCredits + record.Delta));
        
        await cache.RemoveAsync($"{CacheKeyPrefix}{accountId}");
        
        return record;
    }
    
    private const double BaseSocialCredit = 100;
    
    public async Task<double> GetSocialCredit(Guid accountId)
    {
        var cached = await cache.GetAsync<double?>($"{CacheKeyPrefix}{accountId}");
        if (cached.HasValue) return cached.Value;

        var records = await db.SocialCreditRecords
            .Where(x => x.AccountId == accountId && x.Status == SocialCreditRecordStatus.Active)
            .SumAsync(x => x.Delta);
        records += BaseSocialCredit;

        await cache.SetAsync($"{CacheKeyPrefix}{accountId}", records);
        return records;
    }

    public async Task ValidateSocialCredits()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiredRecords = await db.SocialCreditRecords
            .Where(r => r.Status == SocialCreditRecordStatus.Active && r.ExpiredAt.HasValue && r.ExpiredAt <= now)
            .Select(r => new { r.Id, r.AccountId, r.Delta })
            .ToListAsync();

        var groupedExpired = expiredRecords.GroupBy(er => er.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(er => er.Delta));

        foreach (var (accountId, totalDeltaSubtracted) in groupedExpired)
        {
            await db.AccountProfiles
                .Where(p => p.AccountId == accountId)
                .ExecuteUpdateAsync(p => p.SetProperty(v => v.SocialCredits, v => v.SocialCredits - totalDeltaSubtracted));
            await cache.RemoveAsync($"{CacheKeyPrefix}{accountId}");
        }

        await db.SocialCreditRecords
            .Where(r => r.Status == SocialCreditRecordStatus.Active && r.ExpiredAt.HasValue && r.ExpiredAt <= now)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.Status, SocialCreditRecordStatus.Expired));
    }
}
