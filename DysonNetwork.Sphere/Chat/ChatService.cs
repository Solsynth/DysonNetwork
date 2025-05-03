using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Connection;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class ChatService(AppDatabase db, IServiceScopeFactory scopeFactory)
{
    public async Task<Message> SendMessageAsync(Message message, ChatMember sender, ChatRoom room)
    {
        // First complete the save operation
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();
        
        // Then start the delivery process
        // Using ConfigureAwait(false) is correct here since we don't need context to flow
        _ = Task.Run(() => DeliverMessageAsync(message, sender, room))
            .ConfigureAwait(false);
        
        return message;
    }

    public async Task DeliverMessageAsync(Message message, ChatMember sender, ChatRoom room)
    {
        var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var scopedWs = scope.ServiceProvider.GetRequiredService<WebSocketService>();
        var scopedNty = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var roomSubject = room.Realm is not null ? $"{room.Name}, {room.Realm.Name}" : room.Name;
        var tasks = new List<Task>();

        var members = await scopedDb.ChatMembers
            .Where(m => m.ChatRoomId == message.ChatRoomId)
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

    public async Task MarkMessageAsReadAsync(Guid messageId, long roomId, long userId)
    {
        var existingStatus = await db.ChatStatuses
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.Sender.AccountId == userId);
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == userId && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (sender is null) throw new ArgumentException("User is not a member of the chat room.");

        if (existingStatus == null)
        {
            existingStatus = new MessageStatus
            {
                MessageId = messageId,
                SenderId = sender.Id,
            };
            db.ChatStatuses.Add(existingStatus);
        }

        await db.SaveChangesAsync();
    }

    public async Task<bool> GetMessageReadStatus(Guid messageId, long userId)
    {
        return await db.ChatStatuses
            .AnyAsync(x => x.MessageId == messageId && x.Sender.AccountId == userId);
    }

    public async Task<int> CountUnreadMessage(long userId, long chatRoomId)
    {
        var messages = await db.ChatMessages
            .Where(m => m.ChatRoomId == chatRoomId)
            .Select(m => new MessageStatusResponse
            {
                MessageId = m.Id,
                IsRead = m.Statuses.Any(rs => rs.Sender.AccountId == userId)
            })
            .ToListAsync();

        return messages.Count(m => !m.IsRead);
    }

    public async Task<SyncResponse> GetSyncDataAsync(long roomId, long lastSyncTimestamp)
    {
        var timestamp = Instant.FromUnixTimeMilliseconds(lastSyncTimestamp);
        var changes = await db.ChatMessages
            .IgnoreQueryFilters()
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.UpdatedAt > timestamp || m.DeletedAt > timestamp)
            .Select(m => new MessageChange
            {
                MessageId = m.Id,
                Action = m.DeletedAt != null ? "delete" : (m.UpdatedAt == null ? "create" : "update"),
                Message = m.DeletedAt != null ? null : m,
                Timestamp = m.DeletedAt != null ? m.DeletedAt.Value : m.UpdatedAt
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