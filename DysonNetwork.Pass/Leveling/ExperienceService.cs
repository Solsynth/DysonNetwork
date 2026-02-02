using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Leveling;

public class ExperienceService(AppDatabase db)
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

        db.ExperienceRecords.Add(record);
        await db.SaveChangesAsync();
        
        await db.AccountProfiles
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(p => p.SetProperty(v => v.Experience, v => v.Experience + record.Delta));
        
        return record;
    }
}
