using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Realm;

public class RealmService(AppDatabase db, NotificationService nty)
{
    public async Task SendInviteNotify(RealmMember member)
    {
        await nty.SendNotification(member.Account, "invites.realms", "New Realm Invitation", null,
            $"You just got invited to join {member.Realm.Name}");
    }
    
    public async Task<bool> IsMemberWithRole(Guid realmId, Guid accountId, params RealmMemberRole[] requiredRoles)
    {
        if (requiredRoles.Length == 0)
            return false;
            
        var maxRequiredRole = requiredRoles.Max();
        var member = await db.RealmMembers
            .FirstOrDefaultAsync(m => m.RealmId == realmId && m.AccountId == accountId);
        return member?.Role >= maxRequiredRole;
    }
}