using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public partial class ChatController(AppDatabase db, ChatService cs) : ControllerBase
{
    public class MarkMessageReadRequest
    {
        public Guid MessageId { get; set; }
        public long ChatRoomId { get; set; }
    }

    public class SendMessageRequest
    {
        [MaxLength(4096)] public string? Content { get; set; }
        [MaxLength(36)] public string? Nonce { get; set; }
        public List<string>? AttachmentsId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
    }

    [HttpGet("{roomId:long}/members/me")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> GetCurrentIdentity(long roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.Account)
            .FirstOrDefaultAsync();

        if (member == null)
            return NotFound();

        return Ok(member);
    }

    [HttpGet("{roomId:long}/messages")]
    public async Task<ActionResult<List<Message>>> ListMessages(long roomId, [FromQuery] int offset, [FromQuery] int take = 20)
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
            if (member == null || member.Role < ChatMemberRole.Normal)
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
            .Include(m => m.Attachments)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(messages);
    }

    [GeneratedRegex(@"@([A-Za-z0-9_-]+)")]
    private static partial Regex MentionRegex();

    [HttpPost("{roomId:long}/messages")]
    [Authorize]
    public async Task<ActionResult> SendMessage([FromBody] SendMessageRequest request, long roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Content) && (request.AttachmentsId == null || request.AttachmentsId.Count == 0))
            return BadRequest("You cannot send an empty message.");

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .Include(m => m.ChatRoom.Realm)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Normal) return StatusCode(403, "You need to be a normal member to send messages here.");
        currentUser.Profile = null;
        member.Account = currentUser;

        var message = new Message
        {
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

    public class SyncRequest
    {
        [Required]
        public long LastSyncTimestamp { get; set; }
    }

    [HttpGet("{roomId:long}/sync")]
    public async Task<ActionResult<SyncResponse>> GetSyncData([FromBody] SyncRequest request, long roomId)
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