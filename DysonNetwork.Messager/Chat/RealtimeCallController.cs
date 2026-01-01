using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Messager.Chat.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Swashbuckle.AspNetCore.Annotations;

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

    /// <summary>
    /// This endpoint is especially designed for livekit webhooks,
    /// for update the call participates and more.
    /// Learn more at: https://docs.livekit.io/home/server/webhooks/
    /// </summary>
    [HttpPost("webhook")]
    [SwaggerIgnore]
    public async Task<IActionResult> WebhookReceiver()
    {
        using var reader = new StreamReader(Request.Body);
        var postData = await reader.ReadToEndAsync();
        var authHeader = Request.Headers.Authorization.ToString();
        
        await realtime.ReceiveWebhook(postData, authHeader);
    
        return Ok();
    }

    [HttpGet("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> GetOngoingCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<JoinCallResponse>> JoinCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
        var userToken = realtime.GetUserToken(currentUser, ongoingCall.SessionId, isAdmin);

        // Get LiveKit endpoint from configuration
        var endpoint = _config.Endpoint ??
                   throw new InvalidOperationException("LiveKit endpoint configuration is missing");

        // Inject the ChatRoomService
        var chatRoomService = HttpContext.RequestServices.GetRequiredService<ChatRoomService>();

        // Get current participants from the LiveKit service
        var participants = new List<CallParticipant>();
        if (realtime is LiveKitRealtimeService livekitService)
        {
            var roomParticipants = await livekitService.GetRoomParticipantsAsync(ongoingCall.SessionId);
            participants = [];
            
            foreach (var p in roomParticipants)
            {
                var participant = new CallParticipant
                {
                    Identity = p.Identity,
                    Name = p.Name,
                    AccountId = p.AccountId,
                    JoinedAt = p.JoinedAt
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
}
