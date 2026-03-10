using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class DeveloperQuotaService(
    AppDatabase db,
    RemotePublisherService remotePublisherService,
    RemoteRealmService remoteRealmService
)
{
    public async Task<ResourceQuotaResponse<DeveloperBotQuotaRecord>> GetQuotaAsync(SnAccount account)
    {
        var records = await GetOwnedBotRecordsAsync(account.Id);
        var level = account.Profile?.Level ?? 0;
        var total = ResourceQuotaCalculator.GetTieredQuota(level, account.PerkLevel);

        return new ResourceQuotaResponse<DeveloperBotQuotaRecord>
        {
            Total = total,
            Used = records.Count,
            Remaining = Math.Max(0, total - records.Count),
            Level = level,
            PerkLevel = account.PerkLevel,
            Records = records
        };
    }

    private async Task<List<DeveloperBotQuotaRecord>> GetOwnedBotRecordsAsync(Guid accountId)
    {
        var developers = await db.Developers.ToListAsync();
        if (developers.Count == 0)
            return [];

        var publishers = await remotePublisherService.GetPublishersBatch(
            developers.Select(d => d.PublisherId.ToString()).Distinct().ToList()
        );
        var publisherMap = publishers.ToDictionary(p => p.Id);

        var orgRealmIds = publishers
            .Where(p => p.AccountId == null && p.RealmId.HasValue)
            .Select(p => p.RealmId!.Value.ToString())
            .Distinct()
            .ToList();
        var ownedRealmIds = orgRealmIds.Count == 0
            ? new HashSet<Guid>()
            : (await remoteRealmService.GetRealmBatch(orgRealmIds))
                .Where(r => r.AccountId == accountId)
                .Select(r => r.Id)
                .ToHashSet();

        var ownedDeveloperIds = developers
            .Where(d => publisherMap.TryGetValue(d.PublisherId, out var publisher)
                && (
                    publisher.AccountId == accountId
                    || (publisher.RealmId.HasValue && ownedRealmIds.Contains(publisher.RealmId.Value))
                )
            )
            .Select(d => d.Id)
            .ToHashSet();

        if (ownedDeveloperIds.Count == 0)
            return [];

        var bots = await db.BotAccounts
            .Include(b => b.Project)
            .Where(b => ownedDeveloperIds.Contains(b.Project.DeveloperId))
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        var developerNames = developers
            .Where(d => ownedDeveloperIds.Contains(d.Id))
            .ToDictionary(
                d => d.Id,
                d => publisherMap.TryGetValue(d.PublisherId, out var publisher) ? publisher.Name : string.Empty
            );

        return bots
            .Select(b => new DeveloperBotQuotaRecord
            {
                BotId = b.Id,
                Slug = b.Slug,
                ProjectId = b.ProjectId,
                ProjectName = b.Project.Name,
                DeveloperId = b.Project.DeveloperId,
                DeveloperName = developerNames.GetValueOrDefault(b.Project.DeveloperId, string.Empty)
            })
            .ToList();
    }
}
