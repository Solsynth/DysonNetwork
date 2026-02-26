using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Messager.Poll;
using DysonNetwork.Messager.Wallet;
using DysonNetwork.Shared.Models.Embed;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

[ApiController]
[Route("/api/chat")]
public partial class ChatController(
    AppDatabase db,
    ChatService cs,
    ChatRoomService crs,
    DyFileService.DyFileServiceClient files,
    DyAccountService.DyAccountServiceClient accounts,
    DyPaymentService.DyPaymentServiceClient paymentClient,
    DyPollService.DyPollServiceClient pollClient
) : ControllerBase
{
    public class MarkMessageReadRequest
    {
        public Guid ChatRoomId { get; set; }
    }

    public class ChatRoomWsUniversalRequest
    {
        public Guid ChatRoomId { get; set; }
    }

    public class ChatSummaryResponse
    {
        public int UnreadCount { get; set; }
        public SnChatMessage? LastMessage { get; set; }
    }

    [HttpGet("summary")]
    [Authorize]
    public async Task<ActionResult<Dictionary<Guid, ChatSummaryResponse>>> GetChatSummary()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var unreadMessages = await cs.CountUnreadMessageForUser(accountId);
        var lastMessages = await cs.ListLastMessageForUser(accountId);

        var result = unreadMessages.Keys
            .Union(lastMessages.Keys)
            .ToDictionary(
                roomId => roomId,
                roomId => new ChatSummaryResponse
                {
                    UnreadCount = unreadMessages.GetValueOrDefault(roomId),
                    LastMessage = lastMessages.GetValueOrDefault(roomId)
                }
            );

        return Ok(result);
    }

    [HttpGet("unread")]
    [Authorize]
    public async Task<ActionResult<int>> GetTotalUnreadCount()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var unreadMessages = await cs.CountUnreadMessageForUser(accountId);

        var totalUnreadCount = unreadMessages.Values.Sum();

        return Ok(totalUnreadCount);
    }

    public class SendMessageRequest
    {
        [MaxLength(4096)] public string? Content { get; set; }
        [MaxLength(36)] public string? Nonce { get; set; }
        public Guid? FundId { get; set; }
        public Guid? PollId { get; set; }
        public List<string>? AttachmentsId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Guid? RepliedMessageId { get; set; }
        public Guid? ForwardedMessageId { get; set; }
    }

    [HttpGet("{roomId:guid}/messages")]
    public async Task<ActionResult<List<SnChatMessage>>> ListMessages(Guid roomId, [FromQuery] int offset,
        [FromQuery] int take = 20)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var currentUserId = currentUser is null ? (Guid?)null : Guid.Parse(currentUser.Id);

        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();

            var accountId = Guid.Parse(currentUser.Id);
            var member = await db.ChatMembers
                .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null &&
                            m.LeaveAt == null)
                .FirstOrDefaultAsync();
            if (member == null)
                return StatusCode(403, "You are not a member of this chat room.");
        }

        var totalCount = await db.ChatMessages
            .Where(m => m.ChatRoomId == roomId)
            .CountAsync();
        var messages = await db.ChatMessages
            .Where(m => m.ChatRoomId == roomId)
            .OrderByDescending(m => m.CreatedAt)
            .Include(m => m.Sender)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var members = messages.Select(m => m.Sender).DistinctBy(x => x.Id).ToList();
        members = await crs.LoadMemberAccounts(members);

        foreach (var message in messages)
            message.Sender = members.First(x => x.Id == message.SenderId);

        await cs.HydrateMessageReactionsAsync(messages, currentUserId);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(messages);
    }

    [HttpGet("{roomId:guid}/messages/{messageId:guid}")]
    public async Task<ActionResult<SnChatMessage>> GetMessage(Guid roomId, Guid messageId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var currentUserId = currentUser is null ? (Guid?)null : Guid.Parse(currentUser.Id);

        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();

            var accountId = Guid.Parse(currentUser.Id);
            var member = await db.ChatMembers
                .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null &&
                            m.LeaveAt == null)
                .FirstOrDefaultAsync();
            if (member == null)
                return StatusCode(403, "You are not a member of this chat room.");
        }

        var message = await db.ChatMessages
            .Where(m => m.Id == messageId && m.ChatRoomId == roomId)
            .Include(m => m.Sender)
            .FirstOrDefaultAsync();

        if (message is null) return NotFound();

        message.Sender = await crs.LoadMemberAccount(message.Sender);
        await cs.HydrateMessageReactionsAsync([message], currentUserId);

        return Ok(message);
    }


    [GeneratedRegex(@"@(?:u/)?([A-Za-z0-9_-]+)")]
    private static partial Regex MentionRegex();

    /// <summary>
    /// Extracts mentioned users from message content, replies, and forwards
    /// </summary>
    private async Task<List<Guid>> ExtractMentionedUsersAsync(string? content, Guid? repliedMessageId,
        Guid? forwardedMessageId, Guid roomId, Guid? excludeSenderId = null)
    {
        var mentionedUsers = new List<Guid>();

        // Add sender of a replied message
        if (repliedMessageId.HasValue)
        {
            var replyingTo = await db.ChatMessages
                .Where(m => m.Id == repliedMessageId.Value && m.ChatRoomId == roomId)
                .Include(m => m.Sender)
                .Select(m => m.Sender)
                .FirstOrDefaultAsync();
            if (replyingTo != null)
                mentionedUsers.Add(replyingTo.AccountId);
        }

        // Add sender of a forwarded message
        if (forwardedMessageId.HasValue)
        {
            var forwardedMessage = await db.ChatMessages
                .Where(m => m.Id == forwardedMessageId.Value)
                .Select(m => new { m.SenderId })
                .FirstOrDefaultAsync();
            if (forwardedMessage != null)
            {
                mentionedUsers.Add(forwardedMessage.SenderId);
            }
        }

        // Extract mentions from content using regex
        if (!string.IsNullOrWhiteSpace(content))
        {
            var mentionedNames = MentionRegex()
                .Matches(content)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            if (mentionedNames.Count > 0)
            {
                var queryRequest = new DyLookupAccountBatchRequest();
                queryRequest.Names.AddRange(mentionedNames);
                var queryResponse = (await accounts.LookupAccountBatchAsync(queryRequest)).Accounts;
                var mentionedIds = queryResponse.Select(a => Guid.Parse(a.Id)).ToList();

                if (mentionedIds.Count > 0)
                {
                    var mentionedMembers = await db.ChatMembers
                        .Where(m => m.ChatRoomId == roomId && mentionedIds.Contains(m.AccountId))
                        .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                        .Where(m => excludeSenderId == null || m.AccountId != excludeSenderId.Value)
                        .Select(m => m.AccountId)
                        .ToListAsync();
                    mentionedUsers.AddRange(mentionedMembers);
                }
            }
        }

        return mentionedUsers.Distinct().ToList();
    }

    [HttpPost("{roomId:guid}/messages")]
    [Authorize]
    [AskPermission("chat.messages.create")]
    public async Task<ActionResult> SendMessage([FromBody] SendMessageRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0) &&
            !request.FundId.HasValue &&
            !request.PollId.HasValue)
            return BadRequest("You cannot send an empty message.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var member = await crs.GetRoomMember(accountId, roomId);
        if (member == null)
            return StatusCode(403, "You need to be a member to send messages here.");
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, "You has been timed out in this chat.");

        // Validate fund if provided
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await paymentClient.GetWalletFundAsync(new DyGetWalletFundRequest
                {
                    FundId = request.FundId.Value.ToString()
                });
                
                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != member.AccountId.ToString())
                    return BadRequest("You can only share funds that you created.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified fund does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid fund ID.");
            }
        }

        // Validate poll if provided
        if (request.PollId.HasValue)
        {
            try
            {
                var pollResponse = await pollClient.GetPollAsync(new DyGetPollRequest { Id = request.PollId.Value.ToString() });
                // Poll validation is handled by gRPC call
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified poll does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid poll ID.");
            }
        }

        var message = new SnChatMessage
        {
            Type = "text",
            SenderId = member.Id,
            ChatRoomId = roomId,
            Nonce = request.Nonce ?? Guid.NewGuid().ToString(),
            Meta = request.Meta ?? new Dictionary<string, object>(),
        };

        // Add embed for fund if provided
        if (request.FundId.HasValue)
        {
            var fundEmbed = new FundEmbed { Id = request.FundId.Value };
            message.Meta ??= new Dictionary<string, object>();
            if (
                !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                message.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(fundEmbed));
            message.Meta["embeds"] = embeds;
        }

        // Add embed for poll if provided
        if (request.PollId.HasValue)
        {
            var pollResponse = await pollClient.GetPollAsync(new DyGetPollRequest { Id = request.PollId.Value.ToString() });
            var pollEmbed = new PollEmbed { Id = Guid.Parse(pollResponse.Id) };
            message.Meta ??= new Dictionary<string, object>();
            if (
                !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                message.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
            message.Meta["embeds"] = embeds;
        }
        if (request.Content is not null)
            message.Content = request.Content;
        if (request.AttachmentsId is not null)
        {
            var queryRequest = new DyGetFileBatchRequest();
            queryRequest.Ids.AddRange(request.AttachmentsId);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);
            message.Attachments = queryResponse.Files
                .OrderBy(f => request.AttachmentsId.IndexOf(f.Id))
                .Select(SnCloudFileReferenceObject.FromProtoValue)
                .ToList();
        }

        // Validate reply and forward message IDs exist
        if (request.RepliedMessageId.HasValue)
        {
            var repliedMessage = await db.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == request.RepliedMessageId.Value && m.ChatRoomId == roomId);
            if (repliedMessage == null)
                return BadRequest("The message you're replying to does not exist.");

            message.RepliedMessageId = repliedMessage.Id;
        }

        if (request.ForwardedMessageId.HasValue)
        {
            var forwardedMessage = await db.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == request.ForwardedMessageId.Value);
            if (forwardedMessage == null)
                return BadRequest("The message you're forwarding does not exist.");

            message.ForwardedMessageId = forwardedMessage.Id;
        }

        // Extract mentioned users
        message.MembersMentioned = await ExtractMentionedUsersAsync(request.Content, request.RepliedMessageId,
            request.ForwardedMessageId, roomId);

        var result = await cs.SendMessageAsync(message, member, member.ChatRoom);

        return Ok(result);
    }

    [HttpPatch("{roomId:guid}/messages/{messageId:guid}")]
    [Authorize]
    public async Task<ActionResult> UpdateMessage([FromBody] SendMessageRequest request, Guid roomId, Guid messageId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        request.Content = TextSanitizer.Sanitize(request.Content);

        var message = await db.ChatMessages
            .Include(m => m.Sender)
            .Include(message => message.ChatRoom)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == roomId);

        if (message == null) return NotFound();

        var now = SystemClock.Instance.GetCurrentInstant();
        if (message.Sender.AccountId != accountId)
            return StatusCode(403, "You can only edit your own messages.");
        if (message.Sender.TimeoutUntil.HasValue && message.Sender.TimeoutUntil.Value > now)
            return StatusCode(403, "You has been timed out in this chat.");

        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0) &&
            !request.FundId.HasValue &&
            !request.PollId.HasValue)
            return BadRequest("You cannot send an empty message.");

        // Update mentions based on new content and references
        var updatedMentions = await ExtractMentionedUsersAsync(request.Content, request.RepliedMessageId,
            request.ForwardedMessageId, roomId, accountId);
        message.MembersMentioned = updatedMentions;

        // Handle fund embeds for update
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await paymentClient.GetWalletFundAsync(new DyGetWalletFundRequest
                {
                    FundId = request.FundId.Value.ToString()
                });
                
                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != accountId.ToString())
                    return BadRequest("You can only share funds that you created.");

                var fundEmbed = new FundEmbed { Id = request.FundId.Value };
                message.Meta ??= new Dictionary<string, object>();
                if (
                    !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    message.Meta["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
                // Remove all old fund embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "fund"
                );
                embeds.Add(EmbeddableBase.ToDictionary(fundEmbed));
                message.Meta["embeds"] = embeds;
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified fund does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid fund ID.");
            }
        }
        else
        {
            message.Meta ??= new Dictionary<string, object>();
            if (
                !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                message.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
            // Remove all old fund embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "fund");
        }

        // Handle poll embeds for update
        if (request.PollId.HasValue)
        {
            try
            {
                var pollResponse = await pollClient.GetPollAsync(new DyGetPollRequest { Id = request.PollId.Value.ToString() });
                var pollEmbed = new PollEmbed { Id = Guid.Parse(pollResponse.Id) };
                message.Meta ??= new Dictionary<string, object>();
                if (
                    !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    message.Meta["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "poll"
                );
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                message.Meta["embeds"] = embeds;
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified poll does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid poll ID.");
            }
        }
        else
        {
            message.Meta ??= new Dictionary<string, object>();
            if (
                !message.Meta.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                message.Meta["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
            // Remove all old poll embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
        }

        // Call service method to update the message
        await cs.UpdateMessageAsync(
            message,
            request.Meta,
            request.Content,
            request.RepliedMessageId,
            request.ForwardedMessageId,
            request.AttachmentsId
        );

        return Ok(message);
    }

    [HttpDelete("{roomId:guid}/messages/{messageId:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteMessage(Guid roomId, Guid messageId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var message = await db.ChatMessages
            .Include(m => m.Sender)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == roomId);

        if (message == null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (message.Sender.AccountId != accountId)
            return StatusCode(403, "You can only delete your own messages.");

        // Call service method to delete the message
        await cs.DeleteMessageAsync(message);

        return Ok();
    }

    public class MessageReactionRequest
    {
        [MaxLength(256)] public string Symbol { get; set; } = null!;
        public MessageReactionAttitude Attitude { get; set; }
    }

    public static readonly List<string> ReactionsAllowedDefault =
    [
        "thumb_up",
        "thumb_down",
        "just_okay",
        "cry",
        "confuse",
        "clap",
        "laugh",
        "angry",
        "party",
        "pray",
        "heart",
    ];

    [HttpPost("{roomId:guid}/messages/{messageId:guid}/reactions")]
    [Authorize]
    public async Task<ActionResult<SnChatReaction>> ReactMessage(
        Guid roomId,
        Guid messageId,
        [FromBody] MessageReactionRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        if (!ReactionsAllowedDefault.Contains(request.Symbol))
            if (currentUser.PerkSubscription is null)
                return BadRequest("You need subscription to send custom reactions");

        var accountId = Guid.Parse(currentUser.Id);

        var member = await crs.GetRoomMember(accountId, roomId);
        if (member is null)
            return StatusCode(403, "You need to be a member to react to messages here.");

        var message = await db.ChatMessages
            .Where(m => m.Id == messageId && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();

        if (message is null)
            return NotFound();

        var existingReaction = await db.ChatReactions
            .Where(r => r.MessageId == messageId && r.SenderId == member.Id && r.Symbol == request.Symbol)
            .FirstOrDefaultAsync();

        if (existingReaction is not null)
        {
            await cs.RemoveReactionAsync(message.ChatRoom, message, request.Symbol, member);
            return NoContent();
        }

        var reaction = new SnChatReaction
        {
            Symbol = request.Symbol,
            Attitude = request.Attitude,
        };

        var result = await cs.AddReactionAsync(message.ChatRoom, message, reaction, member);

        return Ok(result);
    }

    [HttpDelete("{roomId:guid}/messages/{messageId:guid}/reactions/{symbol}")]
    [Authorize]
    public async Task<ActionResult> RemoveReactionMessage(
        Guid roomId,
        Guid messageId,
        string symbol
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var member = await crs.GetRoomMember(accountId, roomId);
        if (member is null)
            return StatusCode(403, "You need to be a member to remove reaction from messages here.");

        var message = await db.ChatMessages
            .Where(m => m.Id == messageId && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();

        if (message is null)
            return NotFound();

        await cs.RemoveReactionAsync(message.ChatRoom, message, symbol, member);

        return NoContent();
    }

    public class SyncRequest
    {
        [Required] public long LastSyncTimestamp { get; set; }
    }

    public class GlobalSyncResponse
    {
        public List<SnChatMessage> Messages { get; set; } = [];
        public Instant CurrentTimestamp { get; set; }
        public int TotalCount { get; set; }
    }

    [HttpPost("{roomId:guid}/sync")]
    public async Task<ActionResult<SyncResponse>> GetSyncData([FromBody] SyncRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var isMember = await db.ChatMembers
            .AnyAsync(m =>
                m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null);
        if (!isMember)
            return StatusCode(403, "You are not a member of this chat room.");

        var response = await cs.GetSyncDataAsync(roomId, accountId, request.LastSyncTimestamp, 500);
        Response.Headers["X-Total"] = response.TotalCount.ToString();
        return Ok(response);
    }

    [HttpPost("sync")]
    [Authorize]
    public async Task<ActionResult<GlobalSyncResponse>> GetGlobalSyncData([FromBody] SyncRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var lastSyncInstant = Instant.FromUnixTimeMilliseconds(request.LastSyncTimestamp);

        var memberRoomIds = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var messages = await db.ChatMessages
            .Where(m => memberRoomIds.Contains(m.ChatRoomId) && m.CreatedAt > lastSyncInstant)
            .OrderBy(m => m.CreatedAt)
            .Take(500)
            .Include(m => m.Sender)
            .ToListAsync();

        var senders = messages.Select(m => m.Sender).DistinctBy(s => s.Id).ToList();
        senders = await crs.LoadMemberAccounts(senders);

        foreach (var message in messages)
        {
            var sender = senders.FirstOrDefault(s => s.Id == message.SenderId);
            if (sender != null)
            {
                message.Sender = sender;
            }
        }

        await cs.HydrateMessageReactionsAsync(messages, accountId);

        var latestTimestamp = messages.Count > 0
            ? messages.Last().CreatedAt
            : SystemClock.Instance.GetCurrentInstant();

        return Ok(new GlobalSyncResponse
        {
            Messages = messages,
            CurrentTimestamp = latestTimestamp,
            TotalCount = messages.Count
        });
    }


}
