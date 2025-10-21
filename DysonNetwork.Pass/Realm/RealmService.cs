using DysonNetwork.Pass.Localization;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Pass.Realm;

public class RealmService(
    AppDatabase db,
    RingService.RingServiceClient pusher,
    IStringLocalizer<NotificationResource> localizer,
    ICacheService cache
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
        var account = await db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == member.AccountId);
        
        if (account == null) throw new InvalidOperationException("Account not found");
        
        CultureService.SetCultureInfo(account.Language);

        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = account.Id.ToString(),
                Notification = new PushNotification
                {
                    Topic = "invites.realms",
                    Title = localizer["RealmInviteTitle"],
                    Body = localizer["RealmInviteBody", member.Realm.Name],
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
        var account = await db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == member.AccountId);
        if (account != null)
            member.Account = account;
        return member;
    }

    public async Task<List<SnRealmMember>> LoadMemberAccounts(ICollection<SnRealmMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accountsDict = await db.Accounts
            .Include(a => a.Profile)
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a);

        return members.Select(m =>
        {
            if (accountsDict.TryGetValue(m.AccountId, out var account))
                m.Account = account;
            return m;
        }).ToList();
    }
}
