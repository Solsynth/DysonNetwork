using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class ChatRoomService(AppDatabase db, ICacheService cache)
{
    public const string ChatRoomGroupPrefix = "ChatRoom_";
    private const string RoomMembersCacheKeyPrefix = "ChatRoomMembers_";
    private const string ChatMemberCacheKey = "ChatMember_{0}_{1}";
    
    public async Task<List<ChatMember>> ListRoomMembers(Guid roomId)
    {
        var cacheKey = RoomMembersCacheKeyPrefix + roomId;
        var cachedMembers = await cache.GetAsync<List<ChatMember>>(cacheKey);
        if (cachedMembers != null)
            return cachedMembers;
    
        var members = await db.ChatMembers
            .Include(m => m.Account)
            .ThenInclude(m => m.Profile)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .ToListAsync();

        var chatRoomGroup = ChatRoomGroupPrefix + roomId;
        await cache.SetWithGroupsAsync(cacheKey, members,
            [chatRoomGroup], 
            TimeSpan.FromMinutes(5));
    
        return members;
    }
    
    public async Task<ChatMember?> GetChannelMember(Guid accountId, Guid chatRoomId)
    {
        var cacheKey = string.Format(ChatMemberCacheKey, accountId, chatRoomId);
        var member = await cache.GetAsync<ChatMember?>(cacheKey);
        if (member is not null) return member;
        
        member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == chatRoomId)
            .FirstOrDefaultAsync();

        if (member == null) return member;
        var chatRoomGroup = ChatRoomGroupPrefix + chatRoomId;
        await cache.SetWithGroupsAsync(cacheKey, member,
            [chatRoomGroup], 
            TimeSpan.FromMinutes(5));

        return member;
    }
    
    public async Task PurgeRoomMembersCache(Guid roomId)
    {
        var chatRoomGroup = ChatRoomGroupPrefix + roomId;
        await cache.RemoveGroupAsync(chatRoomGroup);
    }

    public async Task<List<ChatRoom>> SortChatRoomByLastMessage(List<ChatRoom> rooms)
    {
        var roomIds = rooms.Select(r => r.Id).ToList();
        var lastMessages = await db.ChatMessages
            .Where(m => roomIds.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => new { RoomId = g.Key, CreatedAt = g.Max(m => m.CreatedAt) })
            .ToDictionaryAsync(g => g.RoomId, m => m.CreatedAt);
    
        var now = SystemClock.Instance.GetCurrentInstant();
        var sortedRooms = rooms
            .OrderByDescending(r => lastMessages.TryGetValue(r.Id, out var time) ? time : now)
            .ToList();
            
        return sortedRooms;
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