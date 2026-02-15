using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass.Ticket;

[ApiController]
[Route("/api/tickets")]
public class TicketController(
    TicketService ticketService,
    PermissionService permissionService
) : ControllerBase
{
    public class CreateTicketRequest
    {
        [Required]
        [MinLength(3)]
        [MaxLength(256)]
        public string Title { get; set; } = null!;

        [MaxLength(16384)]
        public string? Content { get; set; }

        [Required] public TicketType Type { get; set; }

        public TicketPriority Priority { get; set; } = TicketPriority.Medium;

        public List<string>? FileIds { get; set; }
    }

    public class UpdateTicketRequest
    {
        [MinLength(3)]
        [MaxLength(256)]
        public string? Title { get; set; }

        public TicketType? Type { get; set; }

        public TicketPriority? Priority { get; set; }
    }

    public class AddMessageRequest
    {
        [Required]
        [MaxLength(16384)]
        public string Content { get; set; } = null!;
        
        public List<string>? FileIds { get; set; }
    }

    public class AssignRequest
    {
        public Guid? AssigneeId { get; set; }
    }

    public class UpdateStatusRequest
    {
        [Required] public TicketStatus Status { get; set; }
    }

    private async Task<(bool IsAdmin, SnAccount? User)> GetCurrentUserAsync()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return (false, null);

        var isAdmin = await permissionService.HasPermissionAsync(currentUser.Id.ToString(), "tickets.admin");
        return (isAdmin, currentUser);
    }

    [HttpPost("")]
    [Authorize]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnTicket>> CreateTicket([FromBody] CreateTicketRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var ticket = await ticketService.CreateTicketAsync(
                request.Title,
                request.Content,
                request.Type,
                request.Priority,
                currentUser.Id,
                request.FileIds
            );

            return Ok(ticket);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType<List<SnTicket>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<SnTicket>>> GetTickets(
        [FromQuery] Guid? creatorId = null,
        [FromQuery] Guid? assigneeId = null,
        [FromQuery] TicketType? type = null,
        [FromQuery] TicketStatus? status = null,
        [FromQuery] TicketPriority? priority = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view all tickets");

        var tickets = await ticketService.GetTicketsAsync(
            creatorId,
            assigneeId,
            type,
            status,
            priority,
            offset,
            take
        );

        return Ok(tickets);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<List<SnTicket>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnTicket>>> GetMyTickets(
        [FromQuery] TicketStatus? status = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var tickets = await ticketService.GetTicketsAsync(
            creatorId: currentUser.Id,
            status: status,
            offset: offset,
            take: take
        );

        return Ok(tickets);
    }

    [HttpGet("{id}")]
    [Authorize]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> GetTicketById(Guid id)
    {
        var ticket = await ticketService.GetTicketByIdAsync(id);
        if (ticket == null) return NotFound();

        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin && ticket.CreatorId != currentUser?.Id && ticket.AssigneeId != currentUser?.Id)
            return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view this ticket");

        return Ok(ticket);
    }

    [HttpPut("{id}")]
    [Authorize]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> UpdateTicket(Guid id, [FromBody] UpdateTicketRequest request)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to update tickets");

        try
        {
            var ticket = await ticketService.UpdateAsync(
                id,
                request.Title,
                request.Type,
                request.Priority
            );

            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTicket(Guid id)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to delete tickets");

        try
        {
            await ticketService.DeleteTicketAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/messages")]
    [Authorize]
    [ProducesResponseType<SnTicketMessage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicketMessage>> AddMessage(Guid id, [FromBody] AddMessageRequest request)
    {
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to reply to tickets");

        try
        {
            var message = await ticketService.AddMessageAsync(id, currentUser!.Id, request.Content, request.FileIds);
            return Ok(message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/status")]
    [Authorize]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to update ticket status");

        try
        {
            var ticket = await ticketService.UpdateStatusAsync(id, request.Status);
            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> Assign(Guid id, [FromBody] AssignRequest request)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to assign tickets");

        try
        {
            var ticket = await ticketService.AssignAsync(id, request.AssigneeId);
            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("count")]
    [Authorize]
    [ProducesResponseType<object>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<object>> GetTicketsCount(
        [FromQuery] Guid? creatorId = null,
        [FromQuery] Guid? assigneeId = null,
        [FromQuery] TicketStatus? status = null
    )
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, "You do not have permission to view ticket count");

        var count = await ticketService.CountTicketsAsync(creatorId, assigneeId, status);
        return Ok(new { count });
    }
}
