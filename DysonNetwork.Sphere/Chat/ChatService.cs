using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Chat.Realtime;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class ChatService(
    AppDatabase db,
    FileService fs,
    IServiceScopeFactory scopeFactory,
    IRealtimeService realtime
)
{
    public async Task<Message> SendMessageAsync(Message message, ChatMember sender, ChatRoom room)
    {
        if (string.IsNullOrWhiteSpace(message.Nonce)) message.Nonce = Guid.NewGuid().ToString();
        message.CreatedAt = SystemClock.Instance.GetCurrentInstant();
        message.UpdatedAt = message.CreatedAt;

        // First complete the save operation
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        var files = message.Attachments.Distinct().ToList();
        if (files.Count != 0)
        {
            await fs.MarkUsageRangeAsync(files, 1);
            await fs.SetExpiresRangeAsync(files, Duration.FromDays(30));
        }

        // Then start the delivery process
        // Using ConfigureAwait(false) is correct here since we don't need context to flow
        _ = Task.Run(() => DeliverMessageAsync(message, sender, room))
            .ConfigureAwait(false);

        return message;
    }

    public async Task DeliverMessageAsync(Message message, ChatMember sender, ChatRoom room)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var scopedWs = scope.ServiceProvider.GetRequiredService<WebSocketService>();
        var scopedNty = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var roomSubject = room.Realm is not null ? $"{room.Name}, {room.Realm.Name}" : room.Name;
        var tasks = new List<Task>();

        var members = await scopedDb.ChatMembers
            .Where(m => m.ChatRoomId == message.ChatRoomId && m.AccountId != sender.AccountId)
            .Where(m => m.Notify != ChatMemberNotify.None)
            .Where(m => m.Notify != ChatMemberNotify.Mentions ||
                        (message.MembersMentioned != null && message.MembersMentioned.Contains(m.Id)))
            .ToListAsync();

        foreach (var member in members)
        {
            scopedWs.SendPacketToAccount(member.AccountId, new WebSocketPacket
            {
                Type = "messages.new",
                Data = message
            });
            tasks.Add(scopedNty.DeliveryNotification(new Notification
            {
                AccountId = member.AccountId,
                Topic = "messages.new",
                Title = $"{sender.Nick ?? sender.Account.Nick} ({roomSubject})",
            }));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// This method will instant update the LastReadAt field for chat member,
    /// for better performance, using the flush buffer one instead
    /// </summary>
    /// <param name="roomId">The user chat room</param>
    /// <param name="userId">The user id</param>
    /// <exception cref="ArgumentException"></exception>
    public async Task ReadChatRoomAsync(Guid roomId, Guid userId)
    {
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (sender is null) throw new ArgumentException("User is not a member of the chat room.");

        sender.LastReadAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
    }

    public async Task<int> CountUnreadMessage(Guid userId, Guid chatRoomId)
    {
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == chatRoomId)
            .Select(m => new { m.LastReadAt })
            .FirstOrDefaultAsync();
        if (sender?.LastReadAt is null) return 0;

        return await db.ChatMessages
            .Where(m => m.ChatRoomId == chatRoomId)
            .Where(m => m.CreatedAt > sender.LastReadAt)
            .CountAsync();
    }

    public async Task<Dictionary<Guid, int>> CountUnreadMessageForUser(Guid userId)
    {
        var members = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Select(m => new { m.ChatRoomId, m.LastReadAt })
            .ToListAsync();

        var lastReadAt = members.ToDictionary(m => m.ChatRoomId, m => m.LastReadAt);
        var roomsId = lastReadAt.Keys.ToList();

        return await db.ChatMessages
            .Where(m => roomsId.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Count(m => lastReadAt[g.Key] == null || m.CreatedAt > lastReadAt[g.Key])
            );
    }

    public async Task<Dictionary<Guid, Message?>> ListLastMessageForUser(Guid userId)
    {
        var userRooms = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var messages = await db.ChatMessages
            .IgnoreQueryFilters()
            .Include(m => m.Sender)
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile)
            .Where(m => userRooms.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).FirstOrDefault())
            .ToDictionaryAsync(
                m => m!.ChatRoomId,
                m => m
            );

        return messages;
    }

    public async Task<RealtimeCall> CreateCallAsync(ChatRoom room, ChatMember sender)
    {
        var call = new RealtimeCall
        {
            RoomId = room.Id,
            SenderId = sender.Id,
            ProviderName = realtime.ProviderName
        };

        try
        {
            var sessionConfig = await realtime.CreateSessionAsync(room.Id, new Dictionary<string, object>
            {
                { "room_id", room.Id },
                { "user_id", sender.AccountId },
            });

            // Store session details
            call.SessionId = sessionConfig.SessionId;
            call.UpstreamConfig = sessionConfig.Parameters;
        }
        catch (Exception ex)
        {
            // Log the exception but continue with call creation
            throw new InvalidOperationException($"Failed to create {realtime.ProviderName} session: {ex.Message}");
        }

        db.ChatRealtimeCall.Add(call);
        await db.SaveChangesAsync();

        await SendMessageAsync(new Message
        {
            Type = "call.start",
            ChatRoomId = room.Id,
            SenderId = sender.Id,
            Meta = new Dictionary<string, object>
            {
                { "call", call.Id }
            }
        }, sender, room);

        return call;
    }

    public async Task EndCallAsync(Guid roomId)
    {
        var call = await GetCallOngoingAsync(roomId);
        if (call is null) throw new InvalidOperationException("No ongoing call was not found.");

        // End the realtime session if it exists
        if (!string.IsNullOrEmpty(call.SessionId) && !string.IsNullOrEmpty(call.ProviderName))
        {
            try
            {
                var config = new RealtimeSessionConfig
                {
                    SessionId = call.SessionId,
                    Parameters = call.UpstreamConfig
                };

                await realtime.EndSessionAsync(call.SessionId, config);
            }
            catch (Exception ex)
            {
                // Log the exception but continue with call ending
                throw new InvalidOperationException($"Failed to end {call.ProviderName} session: {ex.Message}");
            }
        }

        call.EndedAt = SystemClock.Instance.GetCurrentInstant();
        db.ChatRealtimeCall.Update(call);
        await db.SaveChangesAsync();

        await SendMessageAsync(new Message
        {
            Type = "call.ended",
            ChatRoomId = call.RoomId,
            SenderId = call.SenderId,
            Meta = new Dictionary<string, object>
            {
                { "call", call.Id }
            }
        }, call.Sender, call.Room);
    }

    public async Task<RealtimeCall?> GetCallOngoingAsync(Guid roomId)
    {
        return await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .FirstOrDefaultAsync();
    }

    public async Task<SyncResponse> GetSyncDataAsync(Guid roomId, long lastSyncTimestamp)
    {
        var timestamp = Instant.FromUnixTimeMilliseconds(lastSyncTimestamp);
        var changes = await db.ChatMessages
            .IgnoreQueryFilters()
            .Include(e => e.Sender)
            .Include(e => e.Sender.Account)
            .Include(e => e.Sender.Account.Profile)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.UpdatedAt > timestamp || m.DeletedAt > timestamp)
            .Select(m => new MessageChange
            {
                MessageId = m.Id,
                Action = m.DeletedAt != null ? "delete" : (m.UpdatedAt == m.CreatedAt ? "create" : "update"),
                Message = m.DeletedAt != null ? null : m,
                Timestamp = m.DeletedAt ?? m.UpdatedAt
            })
            .ToListAsync();

        return new SyncResponse
        {
            Changes = changes,
            CurrentTimestamp = SystemClock.Instance.GetCurrentInstant()
        };
    }
}

public class MessageChangeAction
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

public class MessageChange
{
    public Guid MessageId { get; set; }
    public string Action { get; set; } = null!;
    public Message? Message { get; set; }
    public Instant Timestamp { get; set; }
}

public class SyncResponse
{
    public List<MessageChange> Changes { get; set; } = [];
    public Instant CurrentTimestamp { get; set; }
}