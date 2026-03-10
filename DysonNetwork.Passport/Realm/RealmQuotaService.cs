using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Realm;

public class RealmQuotaService(AppDatabase db)
{
    public async Task<ResourceQuotaResponse<RealmQuotaRecord>> GetQuotaAsync(SnAccount account)
    {
        var ownedRealms = await db.Realms
            .Where(r => r.AccountId == account.Id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var level = account.Profile?.Level ?? 0;
        var total = ResourceQuotaCalculator.GetTieredQuota(level, account.PerkLevel);

        return new ResourceQuotaResponse<RealmQuotaRecord>
        {
            Total = total,
            Used = ownedRealms.Count,
            Remaining = Math.Max(0, total - ownedRealms.Count),
            Level = level,
            PerkLevel = account.PerkLevel,
            Records = ownedRealms
                .Select(r => new RealmQuotaRecord
                {
                    Id = r.Id,
                    Slug = r.Slug,
                    Name = r.Name
                })
                .ToList()
        };
    }
}
