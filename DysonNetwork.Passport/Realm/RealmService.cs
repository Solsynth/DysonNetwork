using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
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
        var accountIds = members.Select(m => m.AccountId).ToList();
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

        return members.Select(m =>
        {
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
