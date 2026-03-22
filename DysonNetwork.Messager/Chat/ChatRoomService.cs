using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public class ChatRoomService(
    AppDatabase db,
    ICacheService cache,
    RemoteAccountService remoteAccounts,
    RemoteRealmService remoteRealms
)
{
    public sealed class RoomSubscriptionEntry
    {
        public Guid RoomId { get; set; }
        public Guid MemberId { get; set; }
        public Guid AccountId { get; set; }
        public SnChatMember Member { get; set; } = null!;
        public List<string> DeviceTokens { get; set; } = [];
    }

    public sealed class AccountSubscriptionEntry
    {
        public Guid RoomId { get; set; }
        public Guid MemberId { get; set; }
        public SnChatRoom Room { get; set; } = null!;
        public List<string> DeviceTokens { get; set; } = [];
    }

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
            .Where(m => m.AccountId == accountId && m.ChatRoomId == chatRoomId && m.JoinedAt != null &&
                        m.LeaveAt == null)
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

    public async Task<List<SnChatRoom>> LoadChatRealms(List<SnChatRoom> rooms)
    {
        var realmIds = rooms.Where(r => r.RealmId.HasValue).Select(r => r.RealmId!.Value.ToString()).Distinct().ToList();

        var realms = await remoteRealms.GetRealmBatch(realmIds);
        var realmDict = realms.ToDictionary(r => r.Id, r => r);

        foreach (var room in rooms)
            if (room.RealmId.HasValue && realmDict.TryGetValue(room.RealmId.Value, out var realm))
                room.Realm = realm;

        return rooms;
    }

    public async Task<SnChatRoom> LoadChatRealms(SnChatRoom room)
    {
        var result = await LoadChatRealms(new List<SnChatRoom> { room });
        return result[0];
    }

    public async Task<List<SnChatRoom>> LoadDirectMessageMembers(List<SnChatRoom> rooms, Guid userId)
    {
        var directRoomsId = rooms
            .Where(r => r.Type == ChatRoomType.DirectMessage)
            .Select(r => r.Id)
            .ToList();
        if (directRoomsId.Count == 0) return rooms;

        var members = directRoomsId.Count != 0
            ? await db.ChatMembers
                .Where(m => directRoomsId.Contains(m.ChatRoomId))
                .Where(m => m.AccountId != userId)
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
            .ToListAsync();

        if (members.Count <= 0) return room;

        members = await LoadMemberAccounts(members);
        room.DirectMembers = members.Select(ChatMemberTransmissionObject.FromEntity).ToList();

        return room;
    }

    public async Task<bool> IsChatMember(Guid roomId, Guid accountId)
    {
        return await db.ChatMembers
            .Where(m => m.ChatRoomId == roomId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .AnyAsync();
    }

    public async Task<SnChatMember> LoadMemberAccount(SnChatMember member)
    {
        var account = await remoteAccounts.GetAccount(member.AccountId);
        member.Account = SnAccount.FromProtoValue(account);
        await ApplyRealmIdentity([member]);
        return member;
    }

    public async Task<SnChatMember> HydrateRealmIdentity(SnChatMember member)
    {
        await HydrateRealmIdentity(member, member.ChatRoomId);
        return member;
    }

    public async Task<SnChatMember> HydrateRealmIdentity(SnChatMember member, Guid chatRoomId)
    {
        var roomRealmId = await db.ChatRooms
            .Where(r => r.Id == chatRoomId)
            .Where(r => r.RealmId != null)
            .Select(r => r.RealmId)
            .FirstOrDefaultAsync();
        if (!roomRealmId.HasValue)
            return member;

        var realmMembers = await remoteRealms.LoadMemberAccounts(
            [
                new SnRealmMember
                {
                    RealmId = roomRealmId.Value,
                    AccountId = member.AccountId
                }
            ]
        );
        var realmMember = realmMembers.FirstOrDefault();
        if (realmMember is null)
            return member;

        member.RealmNick = realmMember.Nick;
        member.RealmBio = realmMember.Bio;
        member.RealmExperience = realmMember.Experience;
        member.RealmLevel = realmMember.Level;
        member.RealmLevelingProgress = realmMember.LevelingProgress;
        member.RealmLabel = realmMember.Label;

        return member;
    }

    public async Task<List<SnChatMember>> LoadMemberAccounts(ICollection<SnChatMember> members)
    {
        var accountIds = members.Select(m => m.AccountId).ToList();
        var accounts = (await remoteAccounts.GetAccountBatch(accountIds)).ToDictionary(a => Guid.Parse(a.Id), a => a);

        List<SnChatMember> loadedMembers =
        [
            .. members.Select(m =>
            {
                if (accounts.TryGetValue(m.AccountId, out var account))
                    m.Account = SnAccount.FromProtoValue(account);
                return m;
            })
        ];

        await ApplyRealmIdentity(loadedMembers);
        return loadedMembers;
    }

    public async Task<List<SnChatMember>> HydrateRealmIdentity(ICollection<SnChatMember> members)
    {
        var hydratedMembers = members.ToList();
        await ApplyRealmIdentity(hydratedMembers);
        return hydratedMembers;
    }

    public async Task<List<SnChatMember>> HydrateRealmIdentity(ICollection<SnChatMember> members, Guid chatRoomId)
    {
        var hydratedMembers = members.ToList();
        if (hydratedMembers.Count == 0)
            return hydratedMembers;

        var roomRealmId = await db.ChatRooms
            .Where(r => r.Id == chatRoomId)
            .Where(r => r.RealmId != null)
            .Select(r => r.RealmId)
            .FirstOrDefaultAsync();
        if (!roomRealmId.HasValue)
            return hydratedMembers;

        var placeholders = hydratedMembers
            .Select(m => new SnRealmMember
            {
                RealmId = roomRealmId.Value,
                AccountId = m.AccountId
            })
            .DistinctBy(m => m.AccountId)
            .ToList();
        var realmMembers = await remoteRealms.LoadMemberAccounts(placeholders);
        var realmMap = realmMembers
            .GroupBy(m => m.AccountId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var member in hydratedMembers)
        {
            if (!realmMap.TryGetValue(member.AccountId, out var realmMember)) continue;

            member.RealmNick = realmMember.Nick;
            member.RealmBio = realmMember.Bio;
            member.RealmExperience = realmMember.Experience;
            member.RealmLevel = realmMember.Level;
            member.RealmLevelingProgress = realmMember.LevelingProgress;
            member.RealmLabel = realmMember.Label;
        }

        return hydratedMembers;
    }

    private async Task ApplyRealmIdentity(ICollection<SnChatMember> members)
    {
        var memberList = members.ToList();
        if (memberList.Count == 0) return;

        var roomIds = memberList.Select(m => m.ChatRoomId).Distinct().ToList();
        var roomRealmIds = await db.ChatRooms
            .Where(r => roomIds.Contains(r.Id) && r.RealmId != null)
            .ToDictionaryAsync(r => r.Id, r => r.RealmId!.Value);

        var placeholders = memberList
            .Where(m => roomRealmIds.ContainsKey(m.ChatRoomId))
            .Select(m => new SnRealmMember
            {
                RealmId = roomRealmIds[m.ChatRoomId],
                AccountId = m.AccountId
            })
            .DistinctBy(m => (m.RealmId, m.AccountId))
            .ToList();
        if (placeholders.Count == 0) return;

        var realmMembers = await remoteRealms.LoadMemberAccounts(placeholders);
        var realmMap = realmMembers
            .GroupBy(m => (m.RealmId, m.AccountId))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var member in memberList)
        {
            if (!roomRealmIds.TryGetValue(member.ChatRoomId, out var realmId)) continue;
            if (!realmMap.TryGetValue((realmId, member.AccountId), out var realmMember)) continue;

            member.RealmNick = realmMember.Nick;
            member.RealmBio = realmMember.Bio;
            member.RealmExperience = realmMember.Experience;
            member.RealmLevel = realmMember.Level;
            member.RealmLevelingProgress = realmMember.LevelingProgress;
            member.RealmLabel = realmMember.Label;
        }
    }

    private const string ChatRoomSubscribeKeyPrefix = "chatroom:subscribe:";
    private const string ChatRoomSubscribersGroupPrefix = "chatroom:subscribers:";
    private const string ChatAccountSubscriptionsGroupPrefix = "chatroom:account-subscribers:";
    private const string ChatRoomSubscriptionLockPrefix = "chatroom:subscribe-lock:";
    private const string CacheGlobalKeyPrefix = "dyson:";
    private static readonly TimeSpan ChatRoomSubscriptionTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChatRoomSubscriptionLockTtl = TimeSpan.FromSeconds(5);

    private static string GetChatRoomSubscriptionKey(Guid roomId, Guid memberId) =>
        $"{ChatRoomSubscribeKeyPrefix}{roomId}:{memberId}";

    private static string GetChatRoomSubscribersGroup(Guid roomId) =>
        $"{ChatRoomSubscribersGroupPrefix}{roomId}";

    private static string GetChatAccountSubscriptionsGroup(Guid accountId) =>
        $"{ChatAccountSubscriptionsGroupPrefix}{accountId}";

    private static string GetChatRoomSubscriptionLockKey(Guid roomId, Guid memberId) =>
        $"{ChatRoomSubscriptionLockPrefix}{roomId}:{memberId}";

    private static string GetRawCacheKey(string key) =>
        key.StartsWith(CacheGlobalKeyPrefix, StringComparison.Ordinal)
            ? key[CacheGlobalKeyPrefix.Length..]
            : key;

    private static bool TryParseSubscriptionKey(string key, out Guid roomId, out Guid memberId)
    {
        roomId = Guid.Empty;
        memberId = Guid.Empty;

        var rawKey = GetRawCacheKey(key);
        var parts = rawKey.Split(':');
        if (parts.Length < 4) return false;

        return Guid.TryParse(parts[^2], out roomId) && Guid.TryParse(parts[^1], out memberId);
    }

    private async Task<List<string>> GetActiveSubscriptionKeys(string group)
    {
        var keys = (await cache.GetGroupKeysAsync(group)).Distinct().ToList();
        if (keys.Count == 0) return [];

        var activeKeys = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            var rawKey = GetRawCacheKey(key);
            var (found, tokens) = await cache.GetAsyncWithStatus<List<string>>(rawKey);
            if (found && tokens is { Count: > 0 })
            {
                activeKeys.Add(key);
                continue;
            }

            await cache.RemoveAsync(rawKey);
        }

        return activeKeys;
    }

    public async Task SubscribeChatRoom(SnChatMember member, string deviceId)
    {
        var cacheKey = GetChatRoomSubscriptionKey(member.ChatRoomId, member.Id);
        var group = GetChatRoomSubscribersGroup(member.ChatRoomId);
        var accountGroup = GetChatAccountSubscriptionsGroup(member.AccountId);
        var lockKey = GetChatRoomSubscriptionLockKey(member.ChatRoomId, member.Id);

        await cache.ExecuteWithLockAsync(lockKey, async () =>
        {
            var tokens = await cache.GetAsync<List<string>>(cacheKey) ?? [];
            if (!tokens.Contains(deviceId))
                tokens.Add(deviceId);

            await cache.SetWithGroupsAsync(cacheKey, tokens, [group, accountGroup], ChatRoomSubscriptionTtl);
        }, ChatRoomSubscriptionLockTtl);
    }

    public async Task UnsubscribeChatRoom(SnChatMember member, string deviceId)
    {
        var cacheKey = GetChatRoomSubscriptionKey(member.ChatRoomId, member.Id);
        var lockKey = GetChatRoomSubscriptionLockKey(member.ChatRoomId, member.Id);

        await cache.ExecuteWithLockAsync(lockKey, async () =>
        {
            var tokens = await cache.GetAsync<List<string>>(cacheKey) ?? [];
            tokens.RemoveAll(token => token == deviceId);

            if (tokens.Count == 0)
            {
                await cache.RemoveAsync(cacheKey);
                return;
            }

            await cache.SetAsync(cacheKey, tokens, ChatRoomSubscriptionTtl);
        }, ChatRoomSubscriptionLockTtl);
    }

    public async Task<bool> IsSubscribedChatRoom(Guid roomId, Guid memberId)
    {
        var cacheKey = GetChatRoomSubscriptionKey(roomId, memberId);
        var tokens = await cache.GetAsync<List<string>>(cacheKey);
        return tokens is { Count: > 0 };
    }

    public async Task<List<Guid>> GetSubscribedMembers(Guid roomId)
    {
        var group = GetChatRoomSubscribersGroup(roomId);
        var keys = await GetActiveSubscriptionKeys(group);
        var memberIds = new HashSet<Guid>();
        foreach (var key in keys)
        {
            if (TryParseSubscriptionKey(key, out _, out var memberId))
            {
                memberIds.Add(memberId);
            }
        }

        return memberIds.ToList();
    }

    public async Task<List<RoomSubscriptionEntry>> GetRoomSubscriptions(Guid roomId)
    {
        var group = GetChatRoomSubscribersGroup(roomId);
        var keys = await GetActiveSubscriptionKeys(group);
        if (keys.Count == 0) return [];

        var roomMembers = await ListRoomMembers(roomId);
        var memberMap = roomMembers.ToDictionary(m => m.Id, m => m);
        var result = new List<RoomSubscriptionEntry>(keys.Count);

        foreach (var key in keys)
        {
            if (!TryParseSubscriptionKey(key, out _, out var memberId)) continue;
            if (!memberMap.TryGetValue(memberId, out var member)) continue;

            var tokens = await cache.GetAsync<List<string>>(GetRawCacheKey(key)) ?? [];
            if (tokens.Count == 0) continue;

            result.Add(new RoomSubscriptionEntry
            {
                RoomId = roomId,
                MemberId = member.Id,
                AccountId = member.AccountId,
                Member = member,
                DeviceTokens = tokens
            });
        }

        return result;
    }

    public async Task<List<AccountSubscriptionEntry>> GetAccountSubscriptions(Guid accountId)
    {
        var group = GetChatAccountSubscriptionsGroup(accountId);
        var keys = await GetActiveSubscriptionKeys(group);
        if (keys.Count == 0) return [];

        var subscriptions = new List<(Guid RoomId, Guid MemberId, List<string> Tokens)>(keys.Count);
        foreach (var key in keys)
        {
            if (!TryParseSubscriptionKey(key, out var roomId, out var memberId)) continue;

            var tokens = await cache.GetAsync<List<string>>(GetRawCacheKey(key)) ?? [];
            if (tokens.Count == 0) continue;

            subscriptions.Add((roomId, memberId, tokens));
        }

        var roomIds = subscriptions.Select(s => s.RoomId).Distinct().ToList();
        var rooms = await db.ChatRooms
            .Where(r => roomIds.Contains(r.Id))
            .ToListAsync();
        rooms = await LoadChatRealms(rooms);
        rooms = await LoadDirectMessageMembers(rooms, accountId);

        var roomMap = rooms.ToDictionary(r => r.Id, r => r);
        var result = new List<AccountSubscriptionEntry>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            if (!roomMap.TryGetValue(subscription.RoomId, out var room)) continue;

            result.Add(new AccountSubscriptionEntry
            {
                RoomId = subscription.RoomId,
                MemberId = subscription.MemberId,
                Room = room,
                DeviceTokens = subscription.Tokens
            });
        }

        return result;
    }

    public async Task<List<SnAccount>> GetTopActiveMembers(Guid roomId, Instant startDate, Instant endDate)
    {
        var topMembers = await db.ChatMessages
            .Where(m => m.ChatRoomId == roomId && m.CreatedAt >= startDate && m.CreatedAt < endDate)
            .GroupBy(m => m.Sender.AccountId)
            .Select(g => new { AccountId = g.Key, MessageCount = g.Count() })
            .OrderByDescending(g => g.MessageCount)
            .Take(3)
            .ToListAsync();

        var accountIds = topMembers.Select(t => t.AccountId).ToList();
        var accounts = await remoteAccounts.GetAccountBatch(accountIds);
        return accounts.Select(SnAccount.FromProtoValue).ToList();
    }
}
