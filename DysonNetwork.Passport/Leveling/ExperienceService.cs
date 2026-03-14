using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Leveling;

public class ExperienceService(AppDatabase db, RemoteSubscriptionService subscriptions)
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

        var perkSubscription = await subscriptions.GetPerkSubscription(accountId);
        if (perkSubscription is not null)
        {
            record.BonusMultiplier = perkSubscription.PerkLevel switch
            {
                1 => 1.5,
                2 => 2,
                3 => 2.5,
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
