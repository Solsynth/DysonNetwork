using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeChatConfiguration
{
    public string Provider { get; set; } = null!;
}

[ApiController]
[Route("/chat/realtime")]
public class RealtimeCallController(IConfiguration configuration, AppDatabase db, ChatService cs) : ControllerBase
{
    private readonly RealtimeChatConfiguration _config =
        configuration.GetSection("RealtimeChat").Get<RealtimeChatConfiguration>()!;

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