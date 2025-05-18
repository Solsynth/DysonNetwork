using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DysonNetwork.Sphere.Chat;

public class ChatRoomService(AppDatabase db, IMemoryCache cache)
{
    private const string RoomMembersCacheKey = "ChatRoomMembers_{0}";
    
    public async Task<List<ChatMember>> ListRoomMembers(Guid roomId)
    {
        var cacheKey = string.Format(RoomMembersCacheKey, roomId);
        if (cache.TryGetValue(cacheKey, out List<ChatMember>? cachedMembers))
            return cachedMembers!;
    
        var members = await db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .ToListAsync();
    
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
        cache.Set(cacheKey, members, cacheOptions);
    
        return members;
    }
    
    public void PurgeRoomMembersCache(Guid roomId)
    {
        var cacheKey = string.Format(RoomMembersCacheKey, roomId);
        cache.Remove(cacheKey);
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
                .Where(m => m.LeaveAt == null)
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
            .Where(m => m.LeaveAt == null)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .ToListAsync();
    
        if (members.Count > 0)
            room.DirectMembers = members.Select(ChatMemberTransmissionObject.FromEntity).ToList();
        return room;
    }
    
    public async Task<bool> IsMemberWithRole(Guid roomId, Guid accountId, params ChatMemberRole[] requiredRoles)
    {
        if (requiredRoles.Length == 0)
            return false;
            
        var maxRequiredRole = requiredRoles.Max();
        var member = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == accountId);
        return member?.Role >= maxRequiredRole;
    }
}