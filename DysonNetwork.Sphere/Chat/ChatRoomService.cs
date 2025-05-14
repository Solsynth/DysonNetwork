using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

public class ChatRoomService(AppDatabase db, NotificationService nty)
{
    public async Task SendInviteNotify(ChatMember member)
    {
        await nty.SendNotification(member.Account, "invites.chats", "New Chat Invitation", null,
            $"You just got invited to join {member.ChatRoom.Name}");
    }
    
    public async Task<bool> IsMemberWithRole(Guid roomId, Guid accountId, ChatMemberRole requiredRole)
    {
        var member = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == accountId);
        return member?.Role >= requiredRole;
    }
}