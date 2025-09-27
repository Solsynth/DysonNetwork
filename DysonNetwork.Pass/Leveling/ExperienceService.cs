using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Leveling;

public class ExperienceService(AppDatabase db, SubscriptionService subscriptions, ICacheService cache)
{
    public async Task<ExperienceRecord> AddRecord(string reasonType, string reason, long delta, Guid accountId)
    {
        var record = new ExperienceRecord
        {
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta,
            AccountId = accountId,
        };

        var perkSubscription = await subscriptions.GetPerkSubscriptionAsync(accountId);
        if (perkSubscription is not null)
        {
            record.BonusMultiplier = perkSubscription.Identifier switch
            {
                SubscriptionType.Stellar => 1.5,
                SubscriptionType.Nova => 2,
                SubscriptionType.Supernova => 2,
                _ => 1
            };
            if (record.Delta >= 0)
                record.Delta = (long)Math.Floor(record.Delta * record.BonusMultiplier);
        }

        db.ExperienceRecords.Add(record);
        await db.SaveChangesAsync();
        
        await db.AccountProfiles
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(p => p.SetProperty(v => v.Experience, v => v.Experience + record.Delta));
        
        return record;
    }
}