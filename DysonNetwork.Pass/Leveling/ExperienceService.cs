using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Leveling;

public class ExperienceService(AppDatabase db, SubscriptionService subscriptions)
{
    public async Task<SnExperienceRecord> AddRecord(string reasonType, string reason, long delta, Guid accountId)
    {
        var record = new SnExperienceRecord
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
                SubscriptionType.Supernova => 2.5,
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
