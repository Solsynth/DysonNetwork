using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Messager.Chat.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public class RealtimeChatConfiguration
{
    public string Endpoint { get; set; } = null!;
}

[ApiController]
[Route("/api/chat/realtime")]
public class RealtimeCallController(
    IConfiguration configuration,
    AppDatabase db,
    ChatService cs,
    ChatRoomService crs,
    IRealtimeService realtime
) : ControllerBase
{
    private readonly RealtimeChatConfiguration _config =
        configuration.GetSection("RealtimeChat").Get<RealtimeChatConfiguration>()!;

    [HttpGet("{roomId:guid}/participants")]
    [Authorize]
    public async Task<ActionResult<List<CallParticipant>>> GetParticipants(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null)
            return StatusCode(403, "You need to be a member to view participants.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null || string.IsNullOrEmpty(ongoingCall.SessionId))
            return NotFound("There is no ongoing call in this room.");

        var chatRoomService = HttpContext.RequestServices.GetRequiredService<ChatRoomService>();
        var participants = new List<CallParticipant>();

        if (realtime is LiveKitRealtimeService livekitService)
        {
            var roomParticipants = await livekitService.SyncParticipantsAsync(ongoingCall.SessionId);
            
            foreach (var p in roomParticipants)
            {
                var participant = new CallParticipant
                {
                    Identity = p.Identity,
                    Name = p.Name,
                    AccountId = p.AccountId,
                    JoinedAt = p.JoinedAt,
                    TrackSid = p.TrackSid
                };

                if (p.AccountId.HasValue)
                    participant.Profile = await chatRoomService.GetRoomMember(p.AccountId.Value, roomId);

                participants.Add(participant);
            }
        }

        return Ok(participants);
    }

    [HttpPost("{roomId:guid}/kick/{targetAccountId:guid}")]
    [Authorize]
    public async Task<IActionResult> KickParticipant(Guid roomId, Guid targetAccountId, [FromBody] KickParticipantRequest? request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null)
            return StatusCode(403, "You need to be a member.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null || string.IsNullOrEmpty(ongoingCall.SessionId))
            return NotFound("There is no ongoing call in this room.");

        var isAdmin = member.AccountId == ongoingCall.Room.AccountId || ongoingCall.Room.Type == ChatRoomType.DirectMessage;
        if (!isAdmin)
            return StatusCode(403, "Only room admin can kick participants.");

        var targetMember = await db.ChatMembers
            .Where(m => m.AccountId == targetAccountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (targetMember == null)
            return NotFound("Target member not found.");

        if (targetMember.AccountId == ongoingCall.Room.AccountId)
            return BadRequest("Cannot kick the room owner.");

        if (realtime is LiveKitRealtimeService livekitService)
        {
            var participants = await livekitService.GetRoomParticipantsAsync(ongoingCall.SessionId);
            var targetParticipant = participants.FirstOrDefault(p => p.AccountId == targetAccountId);

            if (targetParticipant != null)
            {
                await livekitService.KickParticipantAsync(ongoingCall.SessionId, targetParticipant.Identity);
            }
        }

        if (request?.BanDurationMinutes > 0)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            targetMember.TimeoutUntil = now.Plus(Duration.FromMinutes(request.BanDurationMinutes.Value));
            targetMember.TimeoutCause = new ChatTimeoutCause
            {
                Type = ChatTimeoutCauseType.ByModerator,
                Reason = request.Reason ?? "Kicked from call",
                SenderId = accountId,
                Since = now
            };
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("{roomId:guid}/mute/{targetAccountId:guid}")]
    [Authorize]
    public async Task<IActionResult> MuteParticipant(Guid roomId, Guid targetAccountId)
    {
        return await _ToggleMuteParticipant(roomId, targetAccountId, true);
    }

    [HttpPost("{roomId:guid}/unmute/{targetAccountId:guid}")]
    [Authorize]
    public async Task<IActionResult> UnmuteParticipant(Guid roomId, Guid targetAccountId)
    {
        return await _ToggleMuteParticipant(roomId, targetAccountId, false);
    }

    private async Task<IActionResult> _ToggleMuteParticipant(Guid roomId, Guid targetAccountId, bool mute)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null)
            return StatusCode(403, "You need to be a member.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null || string.IsNullOrEmpty(ongoingCall.SessionId))
            return NotFound("There is no ongoing call in this room.");

        var isAdmin = member.AccountId == ongoingCall.Room.AccountId || ongoingCall.Room.Type == ChatRoomType.DirectMessage;
        if (!isAdmin)
            return StatusCode(403, "Only room admin can mute participants.");

        if (realtime is LiveKitRealtimeService livekitService)
        {
            var participants = await livekitService.GetRoomParticipantsAsync(ongoingCall.SessionId);
            var targetParticipant = participants.FirstOrDefault(p => p.AccountId == targetAccountId);

            if (targetParticipant == null)
                return NotFound("Target participant not found in call.");

            if (string.IsNullOrEmpty(targetParticipant.TrackSid))
                return BadRequest("No track available to mute.");

            await livekitService.MuteParticipantAsync(
                ongoingCall.SessionId,
                targetParticipant.Identity,
                targetParticipant.TrackSid,
                mute);
        }

        return NoContent();
    }

    [HttpGet("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> GetOngoingCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null)
            return StatusCode(403, "You need to be a member to view call status.");

        var ongoingCall = await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .FirstOrDefaultAsync();
        if (ongoingCall is null) return NotFound();
        ongoingCall.Sender = await crs.LoadMemberAccount(ongoingCall.Sender);
        return Ok(ongoingCall);
    }

    [HttpGet("{roomId:guid}/join")]
    [Authorize]
    public async Task<ActionResult<JoinCallResponse>> JoinCall(Guid roomId, [FromQuery(Name = "tool")] bool isTool = false)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        // Check if the user is a member of the chat room
        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        var now = SystemClock.Instance.GetCurrentInstant();
        if (member == null)
            return StatusCode(403, "You need to be a member to join a call.");
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, "You has been timed out in this chat.");

        // Get ongoing call
        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null)
            return NotFound("There is no ongoing call in this room.");

        // Check if session ID exists
        if (string.IsNullOrEmpty(ongoingCall.SessionId))
            return BadRequest("Call session is not properly configured.");

        var isAdmin = member.AccountId == ongoingCall.Room.AccountId || ongoingCall.Room.Type == ChatRoomType.DirectMessage;
        var userToken = realtime.GetUserToken(currentUser, ongoingCall.SessionId, isAdmin, isTool);

        // Get LiveKit endpoint from configuration
        var endpoint = _config.Endpoint ??
                   throw new InvalidOperationException("LiveKit endpoint configuration is missing");

        // Inject the ChatRoomService
        var chatRoomService = HttpContext.RequestServices.GetRequiredService<ChatRoomService>();

        // Get current participants from the LiveKit service
        var participants = new List<CallParticipant>();
        if (realtime is LiveKitRealtimeService livekitService)
        {
            var roomParticipants = await livekitService.SyncParticipantsAsync(ongoingCall.SessionId);
            participants = [];
            
            foreach (var p in roomParticipants)
            {
                var participant = new CallParticipant
                {
                    Identity = p.Identity,
                    Name = p.Name,
                    AccountId = p.AccountId,
                    JoinedAt = p.JoinedAt,
                    TrackSid = p.TrackSid
                };
            
                // Fetch the ChatMember profile if we have an account ID
                if (p.AccountId.HasValue)
                    participant.Profile = await chatRoomService.GetRoomMember(p.AccountId.Value, roomId);
            
                participants.Add(participant);
            }
        }

        // Create the response model
        var response = new JoinCallResponse
        {
            Provider = realtime.ProviderName,
            Endpoint = endpoint,
            Token = userToken,
            CallId = ongoingCall.Id,
            RoomName = ongoingCall.SessionId,
            IsAdmin = isAdmin,
            Participants = participants
        };

        return Ok(response);
    }

    [HttpPost("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> StartCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();
        
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();
        if (member == null)
            return StatusCode(403, "You need to be a member to start a call.");
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, "You has been timed out in this chat.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is not null) return StatusCode(423, "There is already an ongoing call inside the chatroom.");
        var call = await cs.CreateCallAsync(member.ChatRoom, member);
        return Ok(call);
    }

    [HttpDelete("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> EndCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member == null)
            return StatusCode(403, "You need to be a member to end a call.");

        try
        {
            await cs.EndCallAsync(roomId, member);
            return NoContent();
        }
        catch (Exception exception)
        {
            return BadRequest(exception.Message);
        }
    }
}

// Response model for joining a call
public class JoinCallResponse
{
    /// <summary>
    /// The service provider name (e.g., "LiveKit")
    /// </summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The LiveKit server endpoint
    /// </summary>
    public string Endpoint { get; set; } = null!;

    /// <summary>
    /// Authentication token for the user
    /// </summary>
    public string Token { get; set; } = null!;

    /// <summary>
    /// The call identifier
    /// </summary>
    public Guid CallId { get; set; }

    /// <summary>
    /// The room name in LiveKit
    /// </summary>
    public string RoomName { get; set; } = null!;

    /// <summary>
    /// Whether the user is the admin of the call
    /// </summary>
    public bool IsAdmin { get; set; }
    
    /// <summary>
    /// Current participants in the call
    /// </summary>
    public List<CallParticipant> Participants { get; set; } = new();
}

/// <summary>
/// Represents a participant in a real-time call
/// </summary>
public class CallParticipant
{
    /// <summary>
    /// The participant's identity (username)
    /// </summary>
    public string Identity { get; set; } = null!;
    
    /// <summary>
    /// The participant's display name
    /// </summary>
    public string Name { get; set; } = null!;
    
    /// <summary>
    /// The participant's account ID if available
    /// </summary>
    public Guid? AccountId { get; set; }
    
    /// <summary>
    /// The participant's profile in the chat
    /// </summary>
    public SnChatMember? Profile { get; set; }
    
    /// <summary>
    /// When the participant joined the call
    /// </summary>
    public DateTime JoinedAt { get; set; }

    /// <summary>
    /// The participant's track SID (for muting)
    /// </summary>
    public string? TrackSid { get; set; }
}

public class KickParticipantRequest
{
    public int? BanDurationMinutes { get; set; }
    public string? Reason { get; set; }
}
