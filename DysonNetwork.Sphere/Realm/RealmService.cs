using DysonNetwork.Shared;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(
    AppDatabase db,
    PusherService.PusherServiceClient pusher,
    AccountService.AccountServiceClient accounts,
    IStringLocalizer<NotificationResource> localizer
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
}