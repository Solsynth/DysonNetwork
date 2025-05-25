using DysonNetwork.Sphere.Chat.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeChatConfiguration
{
    public string Endpoint { get; set; } = null!;
}

[ApiController]
[Route("/chat/realtime")]
public class RealtimeCallController(IConfiguration configuration, AppDatabase db, ChatService cs, IRealtimeService realtime) : ControllerBase
{
    private readonly RealtimeChatConfiguration _config =
        configuration.GetSection("RealtimeChat").Get<RealtimeChatConfiguration>()!;

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
        string endpoint = _config.Endpoint ?? 
                     throw new InvalidOperationException("LiveKit endpoint configuration is missing");
        
        // Create response model
        var response = new JoinCallResponse
        {
            Provider = realtime.ProviderName,
            Endpoint = endpoint,
            Token = userToken,
            CallId = ongoingCall.Id,
            RoomName = ongoingCall.SessionId,
            IsAdmin = isAdmin
        };
        
        return Ok(response);
    }

    [HttpPost("{roomId:guid}")]
    [Authorize]
    public async Task<IActionResult> StartCall(Guid roomId)
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
    public async Task<IActionResult> EndCall(Guid roomId)
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
}