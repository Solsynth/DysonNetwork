using DysonNetwork.Shared.Models;
using DysonNetwork.Messager.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public class ChatPinService(
    AppDatabase db,
    ChatRoomService crs,
    ChatService cs
)
{
    public async Task<SnChatMessagePin> PinMessageAsync(
        SnChatRoom room,
        SnChatMember member,
        Guid messageId,
        Instant? expiresAt
    )
    {
        var message = await db.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == room.Id);
        if (message is null)
            throw new InvalidOperationException("Message not found in this room.");

        var existingPin = await db.ChatMessagePins
            .FirstOrDefaultAsync(p => p.ChatRoomId == room.Id && p.MessageId == messageId && p.DeletedAt == null);
        if (existingPin is not null)
            throw new InvalidOperationException("This message is already pinned.");

        var pin = new SnChatMessagePin
        {
            MessageId = messageId,
            ChatRoomId = room.Id,
            PinnedByMemberId = member.Id,
            ExpiresAt = expiresAt,
        };

        db.ChatMessagePins.Add(pin);
        await db.SaveChangesAsync();

        var pinData = new Dictionary<string, object>
        {
            ["pin_id"] = pin.Id,
            ["message_id"] = messageId,
            ["pinned_by_member_id"] = member.Id,
        };
        if (expiresAt.HasValue)
            pinData["expires_at"] = expiresAt.Value.ToUnixTimeMilliseconds();

        await cs.SendSystemMessageAsync(
            room,
            member,
            WebSocketPacketType.MessagePinned,
            null,
            pinData
        );

        return pin;
    }

    public async Task UnpinMessageAsync(
        SnChatRoom room,
        SnChatMember member,
        Guid pinId
    )
    {
        var pin = await db.ChatMessagePins
            .FirstOrDefaultAsync(p => p.Id == pinId && p.ChatRoomId == room.Id && p.DeletedAt == null);
        if (pin is null)
            throw new InvalidOperationException("Pin not found.");

        pin.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        await cs.SendSystemMessageAsync(
            room,
            member,
            WebSocketPacketType.MessageUnpinned,
            null,
            new Dictionary<string, object>
            {
                ["pin_id"] = pin.Id,
                ["message_id"] = pin.MessageId,
            }
        );
    }

    public async Task UnpinMessageByMessageIdAsync(
        SnChatRoom room,
        SnChatMember member,
        Guid messageId
    )
    {
        var pin = await db.ChatMessagePins
            .FirstOrDefaultAsync(p => p.ChatRoomId == room.Id && p.MessageId == messageId && p.DeletedAt == null);
        if (pin is null)
            throw new InvalidOperationException("Pin not found.");

        await UnpinMessageAsync(room, member, pin.Id);
    }

    public async Task<List<SnChatMessagePin>> ListPinsAsync(Guid roomId, bool includeExpired = false)
    {
        var query = db.ChatMessagePins
            .Where(p => p.ChatRoomId == roomId && p.DeletedAt == null);

        if (!includeExpired)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            query = query.Where(p => p.ExpiresAt == null || p.ExpiresAt > now);
        }

        var pins = await query
            .Include(p => p.Message)
                .ThenInclude(m => m!.Sender)
            .Include(p => p.PinnedBy)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var membersNeedingLoad = pins
            .Select(p => p.PinnedBy)
            .Where(m => m.Account == null)
            .DistinctBy(m => m.Id)
            .ToList();
        if (membersNeedingLoad.Count > 0)
        {
            await crs.LoadMemberAccounts(membersNeedingLoad);
        }

        var messageSenders = pins
            .Select(p => p.Message?.Sender)
            .Where(m => m != null && m.Account == null)
            .DistinctBy(m => m!.Id)
            .ToList();
        if (messageSenders.Count > 0)
        {
            await crs.LoadMemberAccounts(messageSenders!);
        }

        return pins;
    }

    public async Task<SnChatMessagePin?> GetPinAsync(Guid pinId, Guid roomId)
    {
        var pin = await db.ChatMessagePins
            .Where(p => p.Id == pinId && p.ChatRoomId == roomId && p.DeletedAt == null)
            .Include(p => p.Message)
                .ThenInclude(m => m!.Sender)
            .Include(p => p.PinnedBy)
            .FirstOrDefaultAsync();

        if (pin is not null)
        {
            if (pin.PinnedBy.Account == null)
                await crs.LoadMemberAccount(pin.PinnedBy);
            if (pin.Message?.Sender?.Account == null && pin.Message?.Sender != null)
                await crs.LoadMemberAccount(pin.Message.Sender);
        }

        return pin;
    }
}
