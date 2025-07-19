using DysonNetwork.Shared;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(
    AppDatabase db,
    PusherService.PusherServiceClient pusher,
    AccountService.AccountServiceClient accounts,
    IStringLocalizer<NotificationResource> localizer,
    AccountClientHelper accountsHelper
)
{
    public async Task SendInviteNotify(RealmMember member)
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

    public async Task<RealmMember> LoadMemberAccount(RealmMember member)
    {
        var account = await accountsHelper.GetAccount(member.AccountId);
        member.Account = Pass.Account.Account.FromProtoValue(account);
        return member;
    }

    public async Task<List<RealmMember>> LoadMemberAccounts(ICollection<RealmMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accounts = (await accountsHelper.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        return members.Select(m =>
        {
            if (accounts.TryGetValue(m.AccountId, out var account))
                m.Account = Pass.Account.Account.FromProtoValue(account);
            return m;
        }).ToList();
    }
}