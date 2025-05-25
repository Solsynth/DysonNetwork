using DysonNetwork.Sphere.Chat.Realtime;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeChatConfiguration
{
    public string Endpoint { get; set; } = null!;
}

[ApiController]
[Route("/chat/realtime")]
public class RealtimeCallController(
    IConfiguration configuration,
    AppDatabase db,
    ChatService cs,
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
    public async Task<ActionResult<RealtimeCall>> GetOngoingCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a member to view call status.");

        var ongoingCall = await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .ThenInclude(c => c.Account)
            .ThenInclude(c => c.Profile)
            .FirstOrDefaultAsync();
        if (ongoingCall is null) return NotFound();
        return Ok(ongoingCall);
    }

    [HttpGet("{roomId:guid}/join")]
    [Authorize]
    public async Task<ActionResult<JoinCallResponse>> JoinCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        // Check if the user is a member of the chat room
        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a member to join a call.");

        // Get ongoing call
        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null)
            return NotFound("There is no ongoing call in this room.");

        // Check if session ID exists
        if (string.IsNullOrEmpty(ongoingCall.SessionId))
            return BadRequest("Call session is not properly configured.");

        var isAdmin = member.Role >= ChatMemberRole.Moderator;
        var userToken = realtime.GetUserToken(currentUser, ongoingCall.SessionId, isAdmin);

        // Get LiveKit endpoint from configuration
        var endpoint = _config.Endpoint ??
                   throw new InvalidOperationException("LiveKit endpoint configuration is missing");

        // Inject the ChatRoomService
        var chatRoomService = HttpContext.RequestServices.GetRequiredService<ChatRoomService>();

        // Get current participants from the LiveKit service
        var participants = new List<CallParticipant>();
        if (realtime is LivekitRealtimeService livekitService)
        {
            var roomParticipants = await livekitService.GetRoomParticipantsAsync(ongoingCall.SessionId);
            participants = new List<CallParticipant>();
            
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
                    participant.Profile = await chatRoomService.GetChannelMember(p.AccountId.Value, roomId);
            
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
    public async Task<ActionResult<RealtimeCall>> StartCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a normal member to start a call.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is not null) return StatusCode(423, "There is already an ongoing call inside the chatroom.");
        var call = await cs.CreateCallAsync(member.ChatRoom, member);
        return Ok(call);
    }

    [HttpDelete("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<RealtimeCall>> EndCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a normal member to end a call.");

        try
        {
            await cs.EndCallAsync(roomId);
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
    public ChatMember? Profile { get; set; }
    
    /// <summary>
    /// When the participant joined the call
    /// </summary>
    public DateTime JoinedAt { get; set; }
}