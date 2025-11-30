using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Autocompletion;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Wallet;
using DysonNetwork.Sphere.WebReader;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/api/chat")]
public partial class ChatController(
    AppDatabase db,
    ChatService cs,
    ChatRoomService crs,
    FileService.FileServiceClient files,
    AccountService.AccountServiceClient accounts,
    AutocompletionService aus,
    PaymentService.PaymentServiceClient paymentClient,
    PollService polls
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
        var currentUser = HttpContext.Items["CurrentUser"] as Account;

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

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(messages);
    }

    [HttpGet("{roomId:guid}/messages/{messageId:guid}")]
    public async Task<ActionResult<SnChatMessage>> GetMessage(Guid roomId, Guid messageId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;

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
                var queryRequest = new LookupAccountBatchRequest();
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
    [RequiredPermission("global", "chat.messages.create")]
    public async Task<ActionResult> SendMessage([FromBody] SendMessageRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0) &&
            !request.FundId.HasValue &&
            !request.PollId.HasValue)
            return BadRequest("You cannot send an empty message.");

        var member = await crs.GetRoomMember(Guid.Parse(currentUser.Id), roomId);
        if (member == null)
            return StatusCode(403, "You need to be a member to send messages here.");

        // Validate fund if provided
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await paymentClient.GetWalletFundAsync(new GetWalletFundRequest
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
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                // Poll validation is handled by the MakePollEmbed method
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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
            var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
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
            var queryRequest = new GetFileBatchRequest();
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        request.Content = TextSanitizer.Sanitize(request.Content);

        var message = await db.ChatMessages
            .Include(m => m.Sender)
            .Include(message => message.ChatRoom)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == roomId);

        if (message == null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (message.Sender.AccountId != accountId)
            return StatusCode(403, "You can only edit your own messages.");

        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0) &&
            !request.FundId.HasValue &&
            !request.PollId.HasValue)
            return BadRequest("You cannot send an empty message.");

        // Validate reply and forward message IDs exist
        if (request.RepliedMessageId.HasValue)
        {
            var repliedMessage = await db.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == request.RepliedMessageId.Value && m.ChatRoomId == roomId);
            if (repliedMessage == null)
                return BadRequest("The message you're replying to does not exist.");
        }

        if (request.ForwardedMessageId.HasValue)
        {
            var forwardedMessage = await db.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == request.ForwardedMessageId.Value);
            if (forwardedMessage == null)
                return BadRequest("The message you're forwarding does not exist.");
        }

        // Update mentions based on new content and references
        var updatedMentions = await ExtractMentionedUsersAsync(request.Content, request.RepliedMessageId,
            request.ForwardedMessageId, roomId, accountId);
        message.MembersMentioned = updatedMentions;

        // Handle fund embeds for update
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await paymentClient.GetWalletFundAsync(new GetWalletFundRequest
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
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
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
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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

    public class SyncRequest
    {
        [Required] public long LastSyncTimestamp { get; set; }
    }

    [HttpPost("{roomId:guid}/sync")]
    public async Task<ActionResult<SyncResponse>> GetSyncData([FromBody] SyncRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var isMember = await db.ChatMembers
            .AnyAsync(m =>
                m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null);
        if (!isMember)
            return StatusCode(403, "You are not a member of this chat room.");

        var response = await cs.GetSyncDataAsync(roomId, request.LastSyncTimestamp, 500);
        Response.Headers["X-Total"] = response.TotalCount.ToString();
        return Ok(response);
    }


    public async Task<ActionResult<List<Shared.Models.Autocompletion>>> ChatAutoComplete(
        [FromBody] AutocompletionRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var isMember = await db.ChatMembers
            .AnyAsync(m =>
                m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null);
        if (!isMember)
            return StatusCode(403, "You are not a member of this chat room.");

        var result = await aus.GetAutocompletion(request.Content, chatId: roomId, limit: 10);
        return Ok(result);
    }
}
