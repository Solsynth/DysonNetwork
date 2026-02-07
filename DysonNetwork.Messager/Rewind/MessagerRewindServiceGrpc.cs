using System.Globalization;
using DysonNetwork.Messager.Chat;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PostReactionAttitude = DysonNetwork.Shared.Proto.PostReactionAttitude;

namespace DysonNetwork.Messager.Rewind;

public class MessagerRewindServiceGrpc(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    ChatRoomService crs
) : RewindService.RewindServiceBase
{
    public override async Task<RewindEvent> GetRewindEvent(
        RequestRewindEvent request,
        ServerCallContext context
    )
    {
        var accountId = Guid.Parse(request.AccountId);
        var year = request.Year;

        var startDate = new LocalDate(year - 1, 12, 26).AtMidnight().InUtc().ToInstant();
        var endDate = new LocalDate(year, 12, 26).AtMidnight().InUtc().ToInstant();

        // Chat data
        var messagesQuery = db
            .ChatMessages.Include(m => m.Sender)
            .Include(m => m.ChatRoom)
            .Where(m => m.CreatedAt >= startDate && m.CreatedAt < endDate)
            .Where(m => m.Sender.AccountId == accountId)
            .AsQueryable();
        var mostMessagedChatInfo = await messagesQuery
            .Where(m => m.ChatRoom.Type == ChatRoomType.Group)
            .GroupBy(m => m.ChatRoomId)
            .OrderByDescending(g => g.Count())
            .Select(g => new { ChatRoom = g.First().ChatRoom, MessageCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostMessagedChat = mostMessagedChatInfo?.ChatRoom;
        var mostMessagedDirectChatInfo = await messagesQuery
            .Where(m => m.ChatRoom.Type == ChatRoomType.DirectMessage)
            .GroupBy(m => m.ChatRoomId)
            .OrderByDescending(g => g.Count())
            .Select(g => new { ChatRoom = g.First().ChatRoom, MessageCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostMessagedDirectChat = mostMessagedDirectChatInfo is not null
            ? await crs.LoadDirectMessageMembers(mostMessagedDirectChatInfo.ChatRoom, accountId)
            : null;

        // Call data
        var callQuery = db
            .ChatRealtimeCall.Include(c => c.Sender)
            .Include(c => c.Room)
            .Where(c => c.CreatedAt >= startDate && c.CreatedAt < endDate)
            .Where(c => c.Sender.AccountId == accountId)
            .AsQueryable();

        var now = SystemClock.Instance.GetCurrentInstant();
        var groupCallRecords = await callQuery
            .Where(c => c.Room.Type == ChatRoomType.Group)
            .Select(c => new
            {
                c.RoomId,
                c.CreatedAt,
                c.EndedAt,
            })
            .ToListAsync();
        var callDurations = groupCallRecords
            .Select(c => new { c.RoomId, Duration = (c.EndedAt ?? now).Minus(c.CreatedAt).Seconds })
            .ToList();
        var mostCalledRoomInfo = callDurations
            .GroupBy(c => c.RoomId)
            .Select(g => new { RoomId = g.Key, TotalDuration = g.Sum(c => c.Duration) })
            .OrderByDescending(g => g.TotalDuration)
            .FirstOrDefault();
        var mostCalledRoom =
            mostCalledRoomInfo != null && mostCalledRoomInfo.RoomId != Guid.Empty
                ? await db.ChatRooms.FindAsync(mostCalledRoomInfo.RoomId)
                : null;

        List<SnAccount>? mostCalledChatTopMembers = null;
        if (mostCalledRoom != null)
            mostCalledChatTopMembers = await crs.GetTopActiveMembers(
                mostCalledRoom.Id,
                startDate,
                endDate
            );

        var directCallRecords = await callQuery
            .Where(c => c.Room.Type == ChatRoomType.DirectMessage)
            .Select(c => new
            {
                c.RoomId,
                c.CreatedAt,
                c.EndedAt,
                c.Room,
            })
            .ToListAsync();
        var directCallDurations = directCallRecords
            .Select(c => new
            {
                c.RoomId,
                c.Room,
                Duration = (c.EndedAt ?? now).Minus(c.CreatedAt).Seconds,
            })
            .ToList();
        var mostCalledDirectRooms = directCallDurations
            .GroupBy(c => c.RoomId)
            .Select(g => new { ChatRoom = g.First().Room, TotalDuration = g.Sum(c => c.Duration) })
            .OrderByDescending(g => g.TotalDuration)
            .Take(3)
            .ToList();

        var accountIds = new List<Guid>();
        foreach (var item in mostCalledDirectRooms)
        {
            var room = await crs.LoadDirectMessageMembers(item.ChatRoom, accountId);
            var otherMember = room.DirectMembers.FirstOrDefault(m => m.AccountId != accountId);
            if (otherMember != null)
                accountIds.Add(otherMember.AccountId);
        }

        var accounts = await remoteAccounts.GetAccountBatch(accountIds);
        var mostCalledAccounts = accounts
            .Zip(
                mostCalledDirectRooms,
                (account, room) =>
                    new Dictionary<string, object?>
                    {
                        ["account"] = account,
                        ["duration"] = room.TotalDuration,
                    }
            )
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["most_messaged_chat"] = mostMessagedChatInfo is not null
                ? new Dictionary<string, object?>
                {
                    ["chat"] = mostMessagedChat,
                    ["message_counts"] = mostMessagedChatInfo.MessageCount,
                }
                : null,
            ["most_messaged_direct_chat"] = mostMessagedDirectChatInfo is not null
                ? new Dictionary<string, object?>
                {
                    ["chat"] = mostMessagedDirectChat,
                    ["message_counts"] = mostMessagedDirectChatInfo.MessageCount,
                }
                : null,
            ["most_called_chat"] = new Dictionary<string, object?>
            {
                ["chat"] = mostCalledRoom,
                ["duration"] = mostCalledRoomInfo?.TotalDuration,
            },
            ["most_called_chat_top_members"] = mostCalledChatTopMembers,
            ["most_called_accounts"] = mostCalledAccounts,
        };

        return new RewindEvent
        {
            ServiceId = "messager",
            AccountId = request.AccountId,
            Data = InfraObjectCoder.ConvertObjectToByteString(data, withoutIgnore: true),
        };
    }
}
