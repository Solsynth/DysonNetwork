using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(AppDatabase db, NotificationService nty, IStringLocalizer<NotificationResource> localizer)
{
    public async Task SendInviteNotify(RealmMember member)
    {
        AccountService.SetCultureInfo(member.Account);
        await nty.SendNotification(
            member.Account,
            "invites.realms",
            localizer["RealmInviteTitle"],
            null,
            localizer["RealmInviteBody", member.Realm.Name],
            actionUri: "/realms"
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
}