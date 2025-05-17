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
    
    public async Task<List<ChatRoom>> LoadDirectMessageMembers(List<ChatRoom> rooms, Guid userId)
    {
        var directRoomsId = rooms
            .Where(r => r.Type == ChatRoomType.DirectMessage)
            .Select(r => r.Id)
            .ToList();
        if (directRoomsId.Count == 0) return rooms;
    
        var directMembers = directRoomsId.Count != 0
            ? await db.ChatMembers
                .Where(m => directRoomsId.Contains(m.ChatRoomId))
                .Where(m => m.AccountId != userId)
                .Include(m => m.Account)
                .Include(m => m.Account.Profile)
                .GroupBy(m => m.ChatRoomId)
                .ToDictionaryAsync(g => g.Key, g => g.ToList())
            : new Dictionary<Guid, List<ChatMember>>();
    
        return rooms.Select(r =>
        {
            if (r.Type == ChatRoomType.DirectMessage && directMembers.TryGetValue(r.Id, out var otherMembers))
                r.DirectMembers = otherMembers.Select(ChatMemberTransmissionObject.FromEntity).ToList();
            return r;
        }).ToList();
    }
    
    public async Task<ChatRoom> LoadDirectMessageMembers(ChatRoom room, Guid userId)
    {
        if (room.Type != ChatRoomType.DirectMessage) return room;
        var members = await db.ChatMembers
            .Where(m => m.ChatRoomId == room.Id && m.AccountId != userId)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .ToListAsync();
    
        if (members.Count > 0)
            room.DirectMembers = members.Select(ChatMemberTransmissionObject.FromEntity).ToList();
        return room;
    }
    
    public async Task<bool> IsMemberWithRole(Guid roomId, Guid accountId, ChatMemberRole requiredRole)
    {
        var member = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == accountId);
        return member?.Role >= requiredRole;
    }
}