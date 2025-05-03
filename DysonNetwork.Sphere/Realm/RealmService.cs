using DysonNetwork.Sphere.Account;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(AppDatabase db, NotificationService nty)
{
    public async Task SendInviteNotify(RealmMember member)
    {
        await nty.SendNotification(member.Account, "invites.realms", "New Realm Invitation", null,
            $"You just got invited to join {member.Realm.Name}");
    }
}