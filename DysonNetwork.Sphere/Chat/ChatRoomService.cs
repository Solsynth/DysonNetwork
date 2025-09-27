using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class ChatRoomService(
    AppDatabase db,
    ICacheService cache,
    AccountClientHelper accountsHelper
)
{
    private const string ChatRoomGroupPrefix = "chatroom:";
    private const string RoomMembersCacheKeyPrefix = "chatroom:members:";
    private const string ChatMemberCacheKey = "chatroom:{0}:member:{1}";

    public async Task<List<SnChatMember>> ListRoomMembers(Guid roomId)
    {
        var cacheKey = RoomMembersCacheKeyPrefix + roomId;
        var cachedMembers = await cache.GetAsync<List<SnChatMember>>(cacheKey);
        if (cachedMembers != null)
            return cachedMembers;

        var members = await db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .ToListAsync();
        members = await LoadMemberAccounts(members);

        var chatRoomGroup = ChatRoomGroupPrefix + roomId;
        await cache.SetWithGroupsAsync(cacheKey, members,
            [chatRoomGroup],
            TimeSpan.FromMinutes(5));

        return members;
    }

    public async Task<SnChatMember?> GetRoomMember(Guid accountId, Guid chatRoomId)
    {
        var cacheKey = string.Format(ChatMemberCacheKey, accountId, chatRoomId);
        var member = await cache.GetAsync<SnChatMember?>(cacheKey);
        if (member is not null) return member;

        member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == chatRoomId)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();

        if (member == null) return member;

        member = await LoadMemberAccount(member);
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

    public async Task<List<SnChatRoom>> SortChatRoomByLastMessage(List<SnChatRoom> rooms)
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

    public async Task<List<SnChatRoom>> LoadDirectMessageMembers(List<SnChatRoom> rooms, Guid userId)
    {
        var directRoomsId = rooms
            .Where(r => r.Type == ChatRoomType.DirectMessage)
            .Select(r => r.Id)
            .ToList();
        if (directRoomsId.Count == 0) return rooms;

        List<SnChatMember> members = directRoomsId.Count != 0
            ? await db.ChatMembers
                .Where(m => directRoomsId.Contains(m.ChatRoomId))
                .Where(m => m.AccountId != userId)
                .Where(m => m.LeaveAt == null)
                .ToListAsync()
            : [];
        members = await LoadMemberAccounts(members);

        Dictionary<Guid, List<SnChatMember>> directMembers = new();
        foreach (var member in members)
        {
            if (!directMembers.ContainsKey(member.ChatRoomId))
                directMembers[member.ChatRoomId] = [];
            directMembers[member.ChatRoomId].Add(member);
        }

        return rooms.Select(r =>
        {
            if (r.Type == ChatRoomType.DirectMessage && directMembers.TryGetValue(r.Id, out var otherMembers))
                r.DirectMembers = otherMembers.Select(ChatMemberTransmissionObject.FromEntity).ToList();
            return r;
        }).ToList();
    }

    public async Task<SnChatRoom> LoadDirectMessageMembers(SnChatRoom room, Guid userId)
    {
        if (room.Type != ChatRoomType.DirectMessage) return room;
        var members = await db.ChatMembers
            .Where(m => m.ChatRoomId == room.Id && m.AccountId != userId)
            .Where(m => m.LeaveAt == null)
            .ToListAsync();

        if (members.Count <= 0) return room;

        members = await LoadMemberAccounts(members);
        room.DirectMembers = members.Select(ChatMemberTransmissionObject.FromEntity).ToList();

        return room;
    }

    public async Task<bool> IsMemberWithRole(Guid roomId, Guid accountId, params int[] requiredRoles)
    {
        if (requiredRoles.Length == 0)
            return false;

        var maxRequiredRole = requiredRoles.Max();
        var member = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == accountId);
        return member?.Role >= maxRequiredRole;
    }

    public async Task<SnChatMember> LoadMemberAccount(SnChatMember member)
    {
        var account = await accountsHelper.GetAccount(member.AccountId);
        member.Account = SnAccount.FromProtoValue(account);
        return member;
    }

    public async Task<List<SnChatMember>> LoadMemberAccounts(ICollection<SnChatMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accounts = (await accountsHelper.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        return [.. members.Select(m =>
        {
            if (accounts.TryGetValue(m.AccountId, out var account))
                m.Account = SnAccount.FromProtoValue(account);
            return m;
        })];
    }

    private const string ChatRoomSubscribeKeyPrefix = "chatroom:subscribe:";

    public async Task SubscribeChatRoom(SnChatMember member)
    {
        var cacheKey = $"{ChatRoomSubscribeKeyPrefix}{member.ChatRoomId}:{member.Id}";
        await cache.SetAsync(cacheKey, true, TimeSpan.FromHours(1));
        await cache.AddToGroupAsync(cacheKey, $"chatroom:subscribers:{member.ChatRoomId}");
    }

    public async Task UnsubscribeChatRoom(SnChatMember member)
    {
        var cacheKey = $"{ChatRoomSubscribeKeyPrefix}{member.ChatRoomId}:{member.Id}";
        await cache.RemoveAsync(cacheKey);
    }

    public async Task<bool> IsSubscribedChatRoom(Guid roomId, Guid memberId)
    {
        var cacheKey = $"{ChatRoomSubscribeKeyPrefix}{roomId}:{memberId}";
        var result = await cache.GetAsync<bool?>(cacheKey);
        return result ?? false;
    }

    public async Task<List<Guid>> GetSubscribedMembers(Guid roomId)
    {
        var group = $"chatroom:subscribers:{roomId}";
        var keys = await cache.GetGroupKeysAsync(group);
        return keys.Select(k => Guid.Parse(k.Split(':').Last())).ToList();
    }
}
