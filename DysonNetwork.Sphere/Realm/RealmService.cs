using DysonNetwork.Shared;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(
    AppDatabase db,
    RingService.RingServiceClient pusher,
    AccountService.AccountServiceClient accounts,
    IStringLocalizer<NotificationResource> localizer,
    AccountClientHelper accountsHelper,
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
            .Select(m => m.Realm!.Id)
            .ToListAsync();

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, realms, TimeSpan.FromMinutes(5));
        
        return realms;
    }
    
    public async Task SendInviteNotify(SnRealmMember member)
    {
        var account = await accounts.GetAccountAsync(new GetAccountRequest { Id = member.AccountId.ToString() });
        CultureService.SetCultureInfo(account);

        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = account.Id,
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
            .FirstOrDefaultAsync(m => m.RealmId == realmId && m.AccountId == accountId);
        return member?.Role >= maxRequiredRole;
    }

    public async Task<SnRealmMember> LoadMemberAccount(SnRealmMember member)
    {
        var account = await accountsHelper.GetAccount(member.AccountId);
        member.Account = SnAccount.FromProtoValue(account);
        return member;
    }

    public async Task<List<SnRealmMember>> LoadMemberAccounts(ICollection<SnRealmMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accounts = (await accountsHelper.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        return members.Select(m =>
        {
            if (accounts.TryGetValue(m.AccountId, out var account))
                m.Account = SnAccount.FromProtoValue(account);
            return m;
        }).ToList();
    }
}