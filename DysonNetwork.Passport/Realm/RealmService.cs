using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using NodaTime;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;

namespace DysonNetwork.Passport.Realm;

public class RealmService(
    AppDatabase db,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    DyAccountService.DyAccountServiceClient accountGrpc
)
{
    private const string CacheKeyPrefix = "account:realms:";

    public async Task<SnRealm?> GetBySlug(string slug)
    {
        var realm = await db.Realms.FirstOrDefaultAsync(r => r.Slug == slug);
        if (realm is null) return null;

        await RefreshBoostState(realm);
        return realm;
    }

    public async Task RefreshBoostState(SnRealm realm, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var cutoff = RealmBoostPolicy.GetActiveCutoff(now);
        var activeContributions = await db.RealmBoostContributions
            .Where(c => c.RealmId == realm.Id && c.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);
        var activeBoostPoints = activeContributions.Sum(c => c.Shares);

        realm.BoostPoints = activeBoostPoints;
    }

    public async Task RefreshBoostStates(ICollection<SnRealm> realms, CancellationToken cancellationToken = default)
    {
        if (realms.Count == 0) return;

        var realmIds = realms.Select(r => r.Id).Distinct().ToList();
        var cutoff = RealmBoostPolicy.GetActiveCutoff(SystemClock.Instance.GetCurrentInstant());
        var activeContributions = await db.RealmBoostContributions
            .Where(c => realmIds.Contains(c.RealmId) && c.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);
        var boostPoints = activeContributions
            .GroupBy(c => c.RealmId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Shares));

        foreach (var realm in realms)
            realm.BoostPoints = boostPoints.GetValueOrDefault(realm.Id, 0m);
    }

    public async Task<SnRealmMember?> GetActiveMember(Guid realmId, Guid accountId)
    {
        return await db.RealmMembers
            .Include(m => m.Label)
            .Include(m => m.Realm)
            .Where(m => m.RealmId == realmId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
    }

    public void EnsureBoostUnlocked(SnRealm realm, int requiredLevel, string message)
    {
        if (realm.BoostLevel < requiredLevel)
            throw new InvalidOperationException(message);
    }

    public async Task<int> GetRealmLabelCount(Guid realmId)
    {
        return await db.RealmLabels.Where(l => l.RealmId == realmId).CountAsync();
    }

    public async Task<List<Guid>> GetUserRealms(Guid accountId)
    {
        var cacheKey = $"{CacheKeyPrefix}{accountId}";
        var (found, cachedRealms) = await cache.GetAsyncWithStatus<List<Guid>>(cacheKey);
        if (found && cachedRealms != null)
            return cachedRealms;

        var realms = await db.RealmMembers
            .Include(m => m.Realm)
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.Realm!.Id)
            .ToListAsync();

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, realms, TimeSpan.FromMinutes(5));

        return realms;
    }

    public async Task SendInviteNotify(SnRealmMember member)
    {
        var account = await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId.ToString() });
        var modelAccount = SnAccount.FromProtoValue(account);

        if (modelAccount == null) throw new InvalidOperationException("Account not found");

        await pusher.SendPushNotificationToUserAsync(
            new DySendPushNotificationToUserRequest
            {
                UserId = modelAccount.Id.ToString(),
                Notification = new DyPushNotification
                {
                    Topic = "invites.realms",
                    Title = localizer.Get("realmInviteTitle", modelAccount.Language),
                    Body = localizer.Get("realmInviteBody", locale: modelAccount.Language, args: new { realmName = member.Realm.Name }),
                    ActionUri = "/realms",
                    IsSavable = true
                }
            }
        );
    }

    public async Task<bool> IsMemberWithRole(Guid realmId, Guid accountId, params int[] requiredRoles)
    {
        if (requiredRoles.Length == 0)
            return false;

        var maxRequiredRole = requiredRoles.Max();
        var member = await db.RealmMembers
            .Where(m => m.RealmId == realmId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        return member?.Role >= maxRequiredRole;
    }

    public async Task<SnRealmMember> LoadMemberAccount(SnRealmMember member)
    {
        if (member.JoinedAt != null && member.LeaveAt == null)
        {
            var actualMember = await GetActiveMember(member.RealmId, member.AccountId);
            if (actualMember is not null)
                member = actualMember;
        }
        else if (member.JoinedAt == null && member.LeaveAt == null && member.Role == 0)
        {
            var actualMember = await GetActiveMember(member.RealmId, member.AccountId);
            if (actualMember is not null)
                member = actualMember;
        }

        try
        {
            var account = SnAccount.FromProtoValue(
                await accountGrpc.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId.ToString() })
            );
            account.Profile = await db.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == member.AccountId);
            member.Account = account;
        }
        catch
        {
            // Keep member without account payload if remote account no longer exists.
        }

        return member;
    }

    public async Task<List<SnRealmMember>> LoadMemberAccounts(ICollection<SnRealmMember> members)
    {
        var incomingMembers = members.ToList();
        if (incomingMembers.Count == 0) return [];

        var groupedRealmIds = incomingMembers.Select(m => m.RealmId).Distinct().ToList();
        var accountIds = incomingMembers.Select(m => m.AccountId).Distinct().ToList();

        var dbMembers = await db.RealmMembers
            .Include(m => m.Label)
            .Include(m => m.Realm)
            .Where(m => groupedRealmIds.Contains(m.RealmId))
            .Where(m => accountIds.Contains(m.AccountId))
            .Where(m => m.LeaveAt == null)
            .ToListAsync();
        var dbMemberMap = dbMembers.ToDictionary(m => (m.RealmId, m.AccountId), m => m);

        var accounts = await accountGrpc.GetAccountBatchAsync(new DyGetAccountBatchRequest
        {
            Id = { accountIds.Select(x => x.ToString()) }
        });
        var accountsDict = accounts.Accounts
            .Select(SnAccount.FromProtoValue)
            .ToDictionary(a => a.Id, a => a);
        var profiles = await db.AccountProfiles
            .Where(p => accountIds.Contains(p.AccountId))
            .ToDictionaryAsync(p => p.AccountId, p => p);

        return incomingMembers.Select(m =>
        {
            if (dbMemberMap.TryGetValue((m.RealmId, m.AccountId), out var dbMember))
            {
                m = dbMember;
            }
            if (accountsDict.TryGetValue(m.AccountId, out var account))
            {
                if (profiles.TryGetValue(m.AccountId, out var profile))
                    account.Profile = profile;
                m.Account = account;
            }
            return m;
        }).ToList();
    }
}
