using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Ticket;

[ApiController]
[Route("/api/admin/tickets/on-call")]
[Authorize]
[ApiFeature("admin.tickets.on-call", Revision = 1)]
public class TicketOnCallAdminController(TicketOnCallService onCall) : ControllerBase
{
    public class AddOnCallAdminRequest
    {
        [Required] public Guid AccountId { get; set; }
    }

    [HttpGet("")]
    [AskPermission(PermissionKeys.TicketsOnCallManage)]
    [ProducesResponseType<List<SnTicketOnCallAdmin>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnTicketOnCallAdmin>>> ListOnCallAdmins()
    {
        return Ok(await onCall.GetOnCallRosterAsync());
    }

    [HttpPost("")]
    [AskPermission(PermissionKeys.TicketsOnCallManage)]
    [ProducesResponseType<SnTicketOnCallAdmin>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnTicketOnCallAdmin>> AddOnCallAdmin(
        [FromBody] AddOnCallAdminRequest request
    )
    {
        try
        {
            return Ok(await onCall.AddOnCallAdminAsync(request.AccountId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError
            {
                Code = "PASSPORT_TICKET_ON_CALL_ADD_FAILED",
                Message = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpDelete("{accountId:guid}")]
    [AskPermission(PermissionKeys.TicketsOnCallManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveOnCallAdmin(Guid accountId)
    {
        await onCall.RemoveOnCallAdminAsync(accountId);
        return NoContent();
    }
}
