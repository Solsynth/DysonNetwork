using System.Globalization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Chat;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Rewind;

public class SphereRewindServiceGrpc(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    ChatRoomService crs,
    Publisher.PublisherService ps
) : RewindService.RewindServiceBase
{
    public override async Task<RewindEvent> GetRewindEvent(RequestRewindEvent request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var year = request.Year;

        var startDate = new LocalDate(year - 1, 12, 26).AtMidnight().InUtc().ToInstant();
        var endDate = new LocalDate(year, 12, 26).AtMidnight().InUtc().ToInstant();

        // Audience data
        var mostLovedPublisherClue =
            await db.PostReactions
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
                .Where(p => p.AccountId == accountId && p.Attitude == Shared.Models.PostReactionAttitude.Positive)
                .GroupBy(p => p.Post.PublisherId)
                .OrderByDescending(g => g.Count())
                .Select(g => new { PublisherId = g.Key, ReactionCount = g.Count() })
                .FirstOrDefaultAsync();
        var mostLovedPublisher = mostLovedPublisherClue is not null
            ? await ps.GetPublisherLoaded(mostLovedPublisherClue.PublisherId)
            : null;

        // Creator data
        var publishers = await db.PublisherMembers
            .Where(pm => pm.AccountId == accountId)
            .Select(pm => pm.PublisherId)
            .ToListAsync();

        var mostLovedAudienceClue =
            await db.PostReactions
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
                .Where(pr =>
                    pr.Attitude == Shared.Models.PostReactionAttitude.Positive &&
                    publishers.Contains(pr.Post.PublisherId))
                .GroupBy(pr => pr.AccountId)
                .OrderByDescending(g => g.Count())
                .Select(g => new { AccountId = g.Key, ReactionCount = g.Count() })
                .FirstOrDefaultAsync();
        var mostLovedAudience = mostLovedAudienceClue is not null
            ? await remoteAccounts.GetAccount(mostLovedAudienceClue.AccountId)
            : null;

        var posts = db.Posts
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => publishers.Contains(p.PublisherId))
            .AsQueryable();
        var postTotalCount = await posts.CountAsync();
        var mostPopularPost = await posts
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .FirstOrDefaultAsync();
        var mostProductiveDay = (await posts.Select(p => new { p.CreatedAt }).ToListAsync())
            .GroupBy(p => p.CreatedAt.ToDateTimeUtc().Date)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Date = g.Key, PostCount = g.Count() })
            .FirstOrDefault();

        // Chat data
        var messagesQuery = db.ChatMessages
            .Include(m => m.Sender)
            .Include(m => m.ChatRoom)
            .Where(m => m.CreatedAt >= startDate && m.CreatedAt < endDate)
            .Where(m => m.Sender.AccountId == accountId)
            .AsQueryable();
        var mostMessagedChat = await messagesQuery
            .Where(m => m.ChatRoom.Type == Shared.Models.ChatRoomType.Group)
            .GroupBy(m => m.ChatRoomId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First().ChatRoom)
            .FirstOrDefaultAsync();
        var mostMessagedDirectChat = await messagesQuery
            .Where(m => m.ChatRoom.Type == Shared.Models.ChatRoomType.DirectMessage)
            .GroupBy(m => m.ChatRoomId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First().ChatRoom)
            .FirstOrDefaultAsync();
        mostMessagedDirectChat = mostMessagedDirectChat is not null
            ? await crs.LoadDirectMessageMembers(mostMessagedDirectChat, accountId)
            : null;

        // Call data
        var callQuery = db.ChatRealtimeCall
            .Include(c => c.Sender)
            .Include(c => c.Room)
            .Where(c => c.CreatedAt >= startDate && c.CreatedAt < endDate)
            .Where(c => c.Sender.AccountId == accountId)
            .AsQueryable();

        var now = SystemClock.Instance.GetCurrentInstant();
        var mostCalledRoom = await callQuery
            .Where(c => c.Room.Type == Shared.Models.ChatRoomType.Group)
            .GroupBy(c => c.RoomId)
            .OrderByDescending(g => g.Sum(c => c.CreatedAt.Minus(c.EndedAt ?? now).Seconds))
            .Select(g => g.First().Room)
            .FirstOrDefaultAsync();

        List<SnAccount>? mostCalledChatTopMembers = null;
        if (mostCalledRoom != null)
            mostCalledChatTopMembers = await crs.GetTopActiveMembers(mostCalledRoom.Id, startDate, endDate);

        var mostCalledDirectRooms = await callQuery
            .Where(c => c.Room.Type == Shared.Models.ChatRoomType.DirectMessage)
            .GroupBy(c => c.RoomId)
            .Select(g => new { ChatRoom = g.First().Room, CallCount = g.Count() })
            .OrderByDescending(g => g.CallCount)
            .Take(3)
            .ToListAsync();

        var accountIds = new List<Guid>();
        foreach (var item in mostCalledDirectRooms)
        {
            var room = await crs.LoadDirectMessageMembers(item.ChatRoom, accountId);
            var otherMember = room.DirectMembers.FirstOrDefault(m => m.AccountId != accountId);
            if (otherMember != null)
                accountIds.Add(otherMember.AccountId);
        }
        var mostCalledAccounts = await remoteAccounts.GetAccountBatch(accountIds);


        var data = new Dictionary<string, object?>
        {
            ["total_count"] = postTotalCount,
            ["most_popular_post"] = mostPopularPost,
            ["most_productive_day"] = mostProductiveDay is not null
                ? new Dictionary<string, object?>
                {
                    ["date"] = mostProductiveDay.Date.ToString(CultureInfo.InvariantCulture),
                    ["post_count"] = mostProductiveDay.PostCount,
                }
                : null,
            ["most_loved_publisher"] = mostLovedPublisherClue is not null
                ? new Dictionary<string, object?>
                {
                    ["publisher"] = mostLovedPublisher,
                    ["upvote_counts"] = mostLovedPublisherClue.ReactionCount,
                }
                : null,
            ["most_loved_audience"] = mostLovedAudienceClue is not null
                ? new Dictionary<string, object?>
                {
                    ["account"] = mostLovedAudience,
                    ["upvote_counts"] = mostLovedAudienceClue.ReactionCount,
                }
                : null,
            ["most_messaged_chat"] = mostMessagedChat,
            ["most_messaged_direct_chat"] = mostMessagedDirectChat,
            ["most_called_chat"] = mostCalledRoom,
            ["most_called_chat_top_members"] = mostCalledChatTopMembers,
            ["most_called_accounts"] = mostCalledAccounts,
        };

        return new RewindEvent
        {
            ServiceId = "sphere",
            AccountId = request.AccountId,
            Data = GrpcTypeHelper.ConvertObjectToByteString(data)
        };
    }
}
