using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public partial class ChatController(AppDatabase db, ChatService cs, FileService fs) : ControllerBase
{
    public class MarkMessageReadRequest
    {
        public Guid ChatRoomId { get; set; }
    }

    public class TypingMessageRequest
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
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var unreadMessages = await cs.CountUnreadMessageForUser(currentUser.Id);
        var lastMessages = await cs.ListLastMessageForUser(currentUser.Id);

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
        var currentUser = HttpContext.Items["CurrentUser"] as Account.Account;

        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();

            var member = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
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
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile)
            .Include(m => m.Attachments)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(messages);
    }

    [HttpGet("{roomId:guid}/messages/{messageId:guid}")]
    public async Task<ActionResult<Message>> GetMessage(Guid roomId, Guid messageId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account.Account;

        var room = await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();

            var member = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (member == null || member.Role < ChatMemberRole.Member)
                return StatusCode(403, "You are not a member of this chat room.");
        }

        var message = await db.ChatMessages
            .Where(m => m.Id == messageId && m.ChatRoomId == roomId)
            .Include(m => m.Sender)
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile)
            .Include(m => m.Attachments)
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
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Content) &&
            (request.AttachmentsId == null || request.AttachmentsId.Count == 0))
            return BadRequest("You cannot send an empty message.");

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .Include(m => m.ChatRoom.Realm)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .FirstOrDefaultAsync();
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
            var attachments = await db.Files
                .Where(f => request.AttachmentsId.Contains(f.Id))
                .ToListAsync();
            message.Attachments = attachments
                .OrderBy(f => request.AttachmentsId.IndexOf(f.Id))
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
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var message = await db.ChatMessages
            .Include(m => m.Sender)
            .Include(m => m.Sender.Account)
            .Include(m => m.Sender.Account.Profile).Include(message => message.Attachments)
            .Include(message => message.ChatRoom)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == roomId);
        if (message == null) return NotFound();

        if (message.Sender.AccountId != currentUser.Id)
            return StatusCode(403, "You can only edit your own messages.");

        if (request.Content is not null)
            message.Content = request.Content;
        if (request.Meta is not null)
            message.Meta = request.Meta;

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

        if (request.AttachmentsId is not null)
        {
            message.Attachments = (await fs.DiffAndMarkFilesAsync(request.AttachmentsId, message.Attachments)).current;
            await fs.DiffAndSetExpiresAsync(request.AttachmentsId, Duration.FromDays(30), message.Attachments);
        }

        message.EditedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(message);
        await db.SaveChangesAsync();
        _ = cs.DeliverMessageAsync(
            message,
            message.Sender,
            message.ChatRoom,
            WebSocketPacketType.MessageUpdate
        );

        return Ok(message);
    }

    [HttpDelete("{roomId:guid}/messages/{messageId:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteMessage(Guid roomId, Guid messageId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var message = await db.ChatMessages
            .Include(m => m.Sender)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ChatRoomId == roomId);
        if (message == null) return NotFound();

        if (message.Sender.AccountId != currentUser.Id)
            return StatusCode(403, "You can only delete your own messages.");

        db.ChatMessages.Remove(message);
        await db.SaveChangesAsync();
        _ = cs.DeliverMessageAsync(
            message,
            message.Sender,
            message.ChatRoom,
            WebSocketPacketType.MessageDelete
        );

        return Ok();
    }

    public class SyncRequest
    {
        [Required] public long LastSyncTimestamp { get; set; }
    }

    [HttpPost("{roomId:guid}/sync")]
    public async Task<ActionResult<SyncResponse>> GetSyncData([FromBody] SyncRequest request, Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var isMember = await db.ChatMembers
            .AnyAsync(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId);
        if (!isMember)
            return StatusCode(403, "You are not a member of this chat room.");

        var response = await cs.GetSyncDataAsync(roomId, request.LastSyncTimestamp);
        return Ok(response);
    }
}