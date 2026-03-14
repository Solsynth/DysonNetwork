using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Realm;

public class RealmExperienceService(AppDatabase db)
{
    public const int TenureDailyXp = 5;
    public const int ChatMessageXp = 2;
    public const int PostCreatedXp = 20;

    public async Task<SnRealmExperienceRecord?> AddRecord(
        Guid realmId,
        Guid accountId,
        string reasonType,
        string reason,
        int delta,
        Duration? cooldown = null,
        CancellationToken cancellationToken = default
    )
    {
        if (delta == 0) return null;

        var member = await db.RealmMembers
            .Where(m => m.RealmId == realmId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync(cancellationToken);
        if (member is null) return null;

        if (cooldown.HasValue)
        {
            var since = SystemClock.Instance.GetCurrentInstant() - cooldown.Value;
            var hasRecent = await db.RealmExperienceRecords
                .Where(r => r.RealmId == realmId && r.AccountId == accountId && r.ReasonType == reasonType)
                .Where(r => r.CreatedAt >= since)
                .AnyAsync(cancellationToken);
            if (hasRecent) return null;
        }
        else
        {
            var exists = await db.RealmExperienceRecords
                .Where(r => r.RealmId == realmId && r.AccountId == accountId && r.ReasonType == reasonType && r.Reason == reason)
                .AnyAsync(cancellationToken);
            if (exists) return null;
        }

        var record = new SnRealmExperienceRecord
        {
            RealmId = realmId,
            AccountId = accountId,
            ReasonType = reasonType,
            Reason = reason,
            Delta = delta
        };

        db.RealmExperienceRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        await db.RealmMembers
            .Where(m => m.RealmId == realmId && m.AccountId == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Experience, m => m.Experience + delta), cancellationToken);

        return record;
    }
}

public class RealmTenureLevelingJob(AppDatabase db, RealmExperienceService experienceService, ILogger<RealmTenureLevelingJob> logger) : Quartz.IJob
{
    public async Task Execute(Quartz.IJobExecutionContext context)
    {
        var today = SystemClock.Instance.GetCurrentInstant().ToDateTimeUtc().Date;

        var members = await db.RealmMembers
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .ToListAsync(context.CancellationToken);

        foreach (var member in members)
        {
            var joinedDate = member.JoinedAt!.Value.ToDateTimeUtc().Date;
            if (joinedDate >= today) continue;

            var reason = today.ToString("yyyy-MM-dd");
            var record = await experienceService.AddRecord(
                member.RealmId,
                member.AccountId,
                "realm.tenure.daily",
                reason,
                RealmExperienceService.TenureDailyXp,
                cooldown: null,
                cancellationToken: context.CancellationToken
            );

            if (record is not null)
                logger.LogDebug("Granted tenure XP to realm member {RealmId}/{AccountId}", member.RealmId, member.AccountId);
        }
    }
}
