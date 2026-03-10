using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherQuotaService(AppDatabase db, RemoteRealmService remoteRealmService)
{
    public async Task<ResourceQuotaResponse<PublisherQuotaRecord>> GetQuotaAsync(SnAccount account)
    {
        var ownedPublishers = await GetOwnedPublishersAsync(account.Id);
        var level = account.Profile?.Level ?? 0;
        var total = ResourceQuotaCalculator.GetPublisherQuota(level, account.PerkLevel);

        return new ResourceQuotaResponse<PublisherQuotaRecord>
        {
            Total = total,
            Used = ownedPublishers.Count,
            Remaining = Math.Max(0, total - ownedPublishers.Count),
            Level = level,
            PerkLevel = account.PerkLevel,
            Records = ownedPublishers
                .Select(p => new PublisherQuotaRecord
                {
                    Id = p.Id,
                    Name = p.Name,
                    Nick = p.Nick,
                    Type = p.Type,
                    RealmId = p.RealmId
                })
                .ToList()
        };
    }

    public async Task<bool> HasCapacityAsync(SnAccount account)
    {
        var quota = await GetQuotaAsync(account);
        return quota.Used < quota.Total;
    }

    private async Task<List<SnPublisher>> GetOwnedPublishersAsync(Guid accountId)
    {
        var directPublishers = await db.Publishers
            .Where(p => p.AccountId == accountId)
            .ToListAsync();

        var organizationalPublishers = await db.Publishers
            .Where(p => p.AccountId == null && p.RealmId != null)
            .ToListAsync();

        if (organizationalPublishers.Count == 0)
            return directPublishers;

        var realmIds = organizationalPublishers
            .Where(p => p.RealmId.HasValue)
            .Select(p => p.RealmId!.Value.ToString())
            .Distinct()
            .ToList();

        var ownedRealmIds = (await remoteRealmService.GetRealmBatch(realmIds))
            .Where(r => r.AccountId == accountId)
            .Select(r => r.Id)
            .ToHashSet();

        directPublishers.AddRange(
            organizationalPublishers.Where(p => p.RealmId.HasValue && ownedRealmIds.Contains(p.RealmId.Value))
        );

        return directPublishers
            .OrderBy(p => p.CreatedAt)
            .ToList();
    }
}
