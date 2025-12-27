using System.Globalization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Chat;
using Grpc.Core;
using JiebaNet.Segmenter;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PostReactionAttitude = DysonNetwork.Shared.Models.PostReactionAttitude;

namespace DysonNetwork.Sphere.Rewind;

public class SphereRewindServiceGrpc(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    ChatRoomService crs,
    Publisher.PublisherService ps
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

        // Audience data
        var mostLovedPublisherClue = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => p.AccountId == accountId && p.Attitude == PostReactionAttitude.Positive)
            .GroupBy(p => p.Post.PublisherId)
            .OrderByDescending(g => g.Count())
            .Select(g => new { PublisherId = g.Key, ReactionCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostLovedPublisher = mostLovedPublisherClue is not null
            ? await ps.GetPublisherLoaded(mostLovedPublisherClue.PublisherId)
            : null;

        // Creator data
        var publishers = await db
            .PublisherMembers.Where(pm => pm.AccountId == accountId)
            .Select(pm => pm.PublisherId)
            .ToListAsync();

        var mostLovedAudienceClue = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(pr =>
                pr.Attitude == PostReactionAttitude.Positive
                && publishers.Contains(pr.Post.PublisherId)
            )
            .GroupBy(pr => pr.AccountId)
            .OrderByDescending(g => g.Count())
            .Select(g => new { AccountId = g.Key, ReactionCount = g.Count() })
            .FirstOrDefaultAsync();
        var mostLovedAudience = mostLovedAudienceClue is not null
            ? await remoteAccounts.GetAccount(mostLovedAudienceClue.AccountId)
            : null;

        var posts = db
            .Posts.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => publishers.Contains(p.PublisherId))
            .AsQueryable();
        var postTotalCount = await posts.CountAsync();
        var postTotalUpvotes = await db
            .PostReactions.Where(a => a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .Where(p => publishers.Contains(p.Post.PublisherId))
            .Where(r => r.Attitude == PostReactionAttitude.Positive)
            .CountAsync();
        var mostPopularPost = await posts
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .FirstOrDefaultAsync();
        var mostProductiveDay = (await posts.Select(p => new { p.CreatedAt }).ToListAsync())
            .GroupBy(p => p.CreatedAt.ToDateTimeUtc().Date)
            .OrderByDescending(g => g.Count())
            .Select(g => new { Date = g.Key, PostCount = g.Count() })
            .FirstOrDefault();
        // Contents to create word cloud
        ConfigManager.ConfigFileBaseDir = @"/app/Resources/cndicts";
        var postContents = await posts
            .Where(p => p.Content != null)
            .Select(p => p.Content)
            .OrderByDescending(p => p!.Length)
            .Take(1000)
            .ToListAsync();
        var segmenter = new JiebaSegmenter();
        var words = segmenter.CutInParallel(postContents, cutAll: false, hmm: false);
        var allWords = words.SelectMany(w => w);
        var topWords = allWords
            .GroupBy(w => w)
            .Select(g => new { Word = g.Key, Count = g.Count() })
            .OrderByDescending(wc => wc.Count)
            .Take(100)
            .ToList();

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
            ["total_post_count"] = postTotalCount,
            ["total_upvote_count"] = postTotalUpvotes,
            ["top_words"] = topWords
                .Select(wc => new Dictionary<string, object?>
                {
                    ["word"] = wc.Word,
                    ["count"] = wc.Count,
                })
                .ToList(),
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
            ServiceId = "sphere",
            AccountId = request.AccountId,
            Data = GrpcTypeHelper.ConvertObjectToByteString(data),
        };
    }
}

