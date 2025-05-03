using DysonNetwork.Sphere.Account;

namespace DysonNetwork.Sphere.Chat;

public class ChatRoomService(NotificationService nty)
{
    public async Task SendInviteNotify(ChatMember member)
    {
        await nty.SendNotification(member.Account, "invites.chats", "New Chat Invitation", null,
            $"You just got invited to join {member.ChatRoom.Name}");
    }
}