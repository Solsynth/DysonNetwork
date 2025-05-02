using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DysonNetwork.Sphere.Storage;
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
        public List<CloudFile>? Attachments { get; set; }
    }

    [GeneratedRegex(@"@([A-Za-z0-9_-]+)")]
    private static partial Regex MentionRegex();

    [HttpPost("{roomId:long}/messages")]
    public async Task<ActionResult> SendMessage([FromBody] SendMessageRequest request, long roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Content) && (request.Attachments == null || request.Attachments.Count == 0))
            return BadRequest("You cannot send an empty message.");

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .Include(m => m.ChatRoom.Realm)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Normal) return StatusCode(403, "You need to be a normal member to send messages here.");

        var message = new Message
        {
            SenderId = member.Id,
            ChatRoomId = roomId,
        };
        if (request.Content is not null)
            message.Content = request.Content;
        if (request.Attachments is not null)
            message.Attachments = request.Attachments;

        if (request.Content is not null)
        {
            var mentioned = MentionRegex()
                .Matches(request.Content)
                .Select(m => m.Groups[1].Value)
                .ToList();
            if (mentioned is not null && mentioned.Count > 0)
            {
                var mentionedMembers = await db.ChatMembers
                   .Where(m => mentioned.Contains(m.Account.Name))
                   .Select(m => m.Id)
                   .ToListAsync();
                message.MembersMetioned = mentionedMembers;
            }
        }

        member.Account = currentUser;
        var result = await cs.SendMessageAsync(message, member, member.ChatRoom);

        return Ok(result);
    }

    public class SyncRequest
    {
        [Required]
        public long LastSyncTimestamp { get; set; }
    }

    [HttpGet("{roomId:long}/sync")]
    public async Task<ActionResult<SyncResponse>> GetSyncData([FromQuery] SyncRequest request, long roomId)
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