using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Content;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
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
    FileService.FileServiceClient files
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
        public Message? LastMessage { get; set; }
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

    public class SendMessageRequest
    {
        [MaxLength(4096)] public string? Content { get; set; }
        [MaxLength(36)] public string? Nonce { get; set; }
        public List<string>? AttachmentsId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Guid? RepliedMessageId { get; set; }
        public Guid? ForwardedMessageId { get; set; }
    }

    [HttpGet("{roomId:guid}/messages")]
    public async Task<ActionResult<List<Message>>> ListMessages(Guid roomId, [FromQuery] int offset,
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
                .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (member == null || member.Role < ChatMemberRole.Member)
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

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(messages);
    }

    [HttpGet("{roomId:guid}/messages/{messageId:guid}")]
    public async Task<ActionResult<Message>> GetMessage(Guid roomId, Guid messageId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;

        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();

            var accountId = Guid.Parse(currentUser.Id);
            var member = await db.ChatMembers
                .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (member == null || member.Role < ChatMemberRole.Member)
                return StatusCode(403, "You are not a member of this chat room.");
        }

        var message = await db.ChatMessages
            .Where(m => m.Id == messageId && m.ChatRoomId == roomId)
            .Include(m => m.Sender)
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile)
            .FirstOrDefaultAsync();

        if (message is null) return NotFound();

        return Ok(message);
    }


    [GeneratedRegex("@([A-Za-z0-9_-]+)")]
    private static partial Regex MentionRegex();

    [HttpPost("{roomId:guid}/messages")]
    [Authorize]
    [RequiredPermission("global", "chat.messages.create")]
    public async Task<ActionResult> SendMessage([FromBody] SendMessageRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0))
            return BadRequest("You cannot send an empty message.");

        var member = await crs.GetRoomMember(Guid.Parse(currentUser.Id), roomId);
        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a normal member to send messages here.");

        var message = new Message
        {
            Type = "text",
            SenderId = member.Id,
            ChatRoomId = roomId,
            Nonce = request.Nonce ?? Guid.NewGuid().ToString(),
            Meta = request.Meta ?? new Dictionary<string, object>(),
        };
        if (request.Content is not null)
            message.Content = request.Content;
        if (request.AttachmentsId is not null)
        {
            var queryRequest = new GetFileBatchRequest();
            queryRequest.Ids.AddRange(request.AttachmentsId);
            var queryResponse = await files.GetFileBatchAsync(queryRequest);
            message.Attachments = queryResponse.Files
                .OrderBy(f => request.AttachmentsId.IndexOf(f.Id))
                .Select(CloudFileReferenceObject.FromProtoValue)
                .ToList();
        }

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

        if (request.Content is not null)
        {
            var mentioned = MentionRegex()
                .Matches(request.Content)
                .Select(m => m.Groups[1].Value)
                .ToList();
            if (mentioned.Count > 0)
            {
                var mentionedMembers = await db.ChatMembers
                    .Where(m => mentioned.Contains(m.Account.Name))
                    .Select(m => m.Id)
                    .ToListAsync();
                message.MembersMentioned = mentionedMembers;
            }
        }

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
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0))
            return BadRequest("You cannot send an empty message.");

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
            .AnyAsync(m => m.AccountId == accountId && m.ChatRoomId == roomId);
        if (!isMember)
            return StatusCode(403, "You are not a member of this chat room.");

        var response = await cs.GetSyncDataAsync(roomId, request.LastSyncTimestamp);
        return Ok(response);
    }
}