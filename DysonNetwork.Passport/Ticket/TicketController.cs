using System.ComponentModel.DataAnnotations;
using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Ticket;

[ApiController]
[Route("/api/tickets")]
public class TicketController(
    TicketService ticketService,
    DyPermissionService.DyPermissionServiceClient permissionService,
    RemoteRingService ringService,
    ILocalizationService localizationService,
    AccountService accountService,
    ILogger<TicketController> logger,
    EmailService emailService,
    RemoteAccountContactService accountContactService,
    IConfiguration configuration
) : ControllerBase
{
    public class CreateTicketRequest
    {
        [Required]
        [MinLength(3)]
        [MaxLength(256)]
        public string Title { get; set; } = null!;

        [MaxLength(16384)] public string Content { get; set; } = null!;

        [Required] public TicketType Type { get; set; }

        public TicketPriority Priority { get; set; } = TicketPriority.Medium;

        public List<string>? FileIds { get; set; }
        public List<string?>? Resources { get; set; }
    }

    public class UpdateTicketRequest
    {
        [MinLength(3)]
        [MaxLength(256)]
        public string? Title { get; set; }

        public TicketType? Type { get; set; }

        public TicketPriority? Priority { get; set; }

        public List<string?>? Resources { get; set; }
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
        if (currentUser.IsSuperuser) return (true, currentUser);
        var resp = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
        {
            Actor = currentUser.Id.ToString(),
            Key = "tickets.admin"
        });
        return (resp.HasPermission, currentUser);
    }

    private static bool IsSelfScopedTicketQuery(
        SnAccount currentUser,
        Guid? creatorId,
        Guid? assigneeId
    )
    {
        var hasScope = false;

        if (creatorId.HasValue)
        {
            hasScope = true;
            if (creatorId.Value != currentUser.Id) return false;
        }

        if (assigneeId.HasValue)
        {
            hasScope = true;
            if (assigneeId.Value != currentUser.Id) return false;
        }

        return hasScope;
    }

    private async Task NotifyTicketStatusChangedAsync(SnTicket ticket, TicketStatus oldStatus, TicketStatus newStatus, SnAccount updater)
    {
        var superusers = await accountService.GetAllSuperusersAsync();
        var otherSuperusers = superusers.Where(s => s.Id != updater.Id).ToList();

        var ticketCreator = ticket.Creator;
        var ticketAssignee = ticket.Assignee;

        var interestedUsers = new List<SnAccount>();
        if (ticketCreator.Id != updater.Id) interestedUsers.Add(ticketCreator);
        if (ticketAssignee != null && ticketAssignee.Id != updater.Id) interestedUsers.Add(ticketAssignee);
        if (!updater.IsSuperuser) interestedUsers.AddRange(otherSuperusers);

        var uniqueUsers = interestedUsers.DistinctBy(u => u.Id).ToList();

        foreach (var user in uniqueUsers)
        {
            var locale = user.Language;
            var title = localizationService.Get("ticketStatusUpdatedTitle", locale);
            var body = localizationService.Get("ticketStatusUpdatedBody", locale, new
            {
                ticketTitle = ticket.Title,
                oldStatus = oldStatus.ToString(),
                newStatus = newStatus.ToString(),
                updaterName = updater.Nick
            });

            _ = ringService.SendPushNotificationToUser(user.Id.ToString(), "ticket.status", title, null, body);
        }
    }

    private async Task NotifyTicketAssignedAsync(SnTicket ticket, SnAccount? oldAssignee, SnAccount newAssignee, SnAccount assigner)
    {
        var superusers = await accountService.GetAllSuperusersAsync();
        var otherSuperusers = superusers.Where(s => s.Id != assigner.Id).ToList();

        var ticketCreator = ticket.Creator;

        var interestedUsers = new List<SnAccount>();
        if (ticketCreator.Id != assigner.Id) interestedUsers.Add(ticketCreator);
        if (oldAssignee != null && oldAssignee.Id != assigner.Id) interestedUsers.Add(oldAssignee);
        if (!assigner.IsSuperuser) interestedUsers.AddRange(otherSuperusers);

        var uniqueUsers = interestedUsers.DistinctBy(u => u.Id).ToList();

        foreach (var user in uniqueUsers)
        {
            var locale = user.Language;
            var title = localizationService.Get("ticketAssignedTitle", locale);
            var body = localizationService.Get("ticketAssignedBody", locale, new
            {
                ticketTitle = ticket.Title,
                assigneeName = newAssignee.Nick,
                assignerName = assigner.Nick
            });

            _ = ringService.SendPushNotificationToUser(user.Id.ToString(), "ticket.assign", title, null, body);
        }
    }

    private async Task NotifyTicketNewMessageAsync(SnTicket ticket, SnAccount sender)
    {
        var superusers = await accountService.GetAllSuperusersAsync();
        var otherSuperusers = superusers.Where(s => s.Id != sender.Id).ToList();

        var ticketCreator = ticket.Creator;
        var ticketAssignee = ticket.Assignee;

        var interestedUsers = new List<SnAccount>();
        if (ticketCreator.Id != sender.Id) interestedUsers.Add(ticketCreator);
        if (ticketAssignee != null && ticketAssignee.Id != sender.Id) interestedUsers.Add(ticketAssignee);
        if (!sender.IsSuperuser) interestedUsers.AddRange(otherSuperusers);

        var uniqueUsers = interestedUsers.DistinctBy(u => u.Id).ToList();

        foreach (var user in uniqueUsers)
        {
            var locale = user.Language;
            var title = localizationService.Get("ticketNewMessageTitle", locale);
            var body = localizationService.Get("ticketNewMessageBody", locale, new
            {
                senderName = sender.Nick,
                ticketTitle = ticket.Title
            });

            _ = ringService.SendPushNotificationToUser(user.Id.ToString(), "ticket.message", title, null, body);
        }
    }

    private async Task NotifyTicketCreatedAsync(SnTicket ticket)
    {
        try
        {
            var superusers = await accountService.GetAllSuperusersAsync();

            foreach (var superuser in superusers)
            {
                var locale = superuser.Language;
                var title = localizationService.Get("ticketCreatedTitle", locale);
                var body = localizationService.Get("ticketCreatedBody", locale, new
                {
                    creatorName = ticket.Creator.Nick,
                    ticketTitle = ticket.Title
                });

                try
                {
                    await ringService.SendPushNotificationToUser(
                        superuser.Id.ToString(),
                        "ticket.created",
                        title,
                        null,
                        body
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to send new-ticket notification for ticket {TicketId} to superuser {SuperuserId}",
                        ticket.Id,
                        superuser.Id
                    );
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to notify superusers about new ticket {TicketId}", ticket.Id);
        }
    }

    private async Task SendTicketCreatorEmailAsync(
        SnTicket ticket,
        string subject,
        string message,
        string? latestMessage = null
    )
    {
        try
        {
            var contact = (await accountContactService.ListContactsAsync(
                    ticket.CreatorId,
                    AccountContactType.Email,
                    verifiedOnly: true
                ))
                .OrderByDescending(contact => contact.IsPrimary)
                .FirstOrDefault();
            if (contact is null) return;

            var recipientName = string.IsNullOrWhiteSpace(ticket.Creator.Nick)
                ? ticket.Creator.Name
                : ticket.Creator.Nick;
            var siteUrl = configuration.GetValue<string>("SiteUrl")?.TrimEnd('/');
            var link = $"{siteUrl}/tickets/{ticket.Id}";

            await emailService.SendTemplatedEmailAsync(
                recipientName,
                contact.Content,
                subject,
                "TicketUpdate",
                new
                {
                    nick = recipientName,
                    ticketTitle = ticket.Title,
                    message,
                    latestMessage,
                    link
                },
                ticket.Creator.Language
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to email ticket update for ticket {TicketId} to its creator", ticket.Id);
        }
    }

    [HttpPost("")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsCreate)]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnTicket>> CreateTicket([FromBody] CreateTicketRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var ticket = await ticketService.CreateTicketAsync(
                request.Title,
                request.Content,
                request.Type,
                request.Priority,
                currentUser.Id,
                request.FileIds,
                request.Resources
            );

            await NotifyTicketCreatedAsync(ticket);

            return Ok(ticket);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_CREATE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
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
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (currentUser == null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (!isAdmin && !IsSelfScopedTicketQuery(currentUser, creatorId, assigneeId))
            return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to view these tickets.", forbidden: true));

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
        if (ticket == null) return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin && ticket.CreatorId != currentUser?.Id && ticket.AssigneeId != currentUser?.Id)
            return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to view this ticket.", forbidden: true));

        return Ok(ticket);
    }

    [HttpPut("{id}")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsUpdate)]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> UpdateTicket(Guid id, [FromBody] UpdateTicketRequest request)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to update tickets.", forbidden: true));

        try
        {
            var ticket = await ticketService.UpdateAsync(
                id,
                request.Title,
                request.Type,
                request.Priority,
                request.Resources
            );

            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_UPDATE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTicket(Guid id)
    {
        var (isAdmin, _) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to delete tickets.", forbidden: true));

        try
        {
            await ticketService.DeleteTicketAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_DELETE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{id:guid}/messages")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsMessagesCreate)]
    [ProducesResponseType<SnTicketMessage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicketMessage>> AddMessage(Guid id, [FromBody] AddMessageRequest request)
    {
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to reply to tickets.", forbidden: true));

        try
        {
            var ticket = await ticketService.GetTicketByIdAsync(id);
            if (ticket == null) return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

            var message = await ticketService.AddMessageAsync(id, currentUser!.Id, request.Content, request.FileIds);

            await NotifyTicketNewMessageAsync(ticket, currentUser);

            return Ok(message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_MESSAGE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{id:guid}/status")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsStatusUpdate)]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to update ticket status.", forbidden: true));

        try
        {
            var existingTicket = await ticketService.GetTicketByIdAsync(id);
            if (existingTicket == null) return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

            var oldStatus = existingTicket.Status;
            var ticket = await ticketService.UpdateStatusAsync(id, request.Status);

            if (oldStatus != request.Status)
            {
                await NotifyTicketStatusChangedAsync(ticket, oldStatus, request.Status, currentUser!);

                if (request.Status is TicketStatus.Resolved or TicketStatus.Closed)
                {
                    await SendTicketCreatorEmailAsync(
                        ticket,
                        localizationService.Get("ticketStatusUpdatedTitle", ticket.Creator.Language),
                        localizationService.Get("ticketStatusUpdatedBody", ticket.Creator.Language, new
                        {
                            ticketTitle = ticket.Title,
                            oldStatus = oldStatus.ToString(),
                            newStatus = request.Status.ToString(),
                            updaterName = currentUser!.Nick
                        })
                    );
                }
                else if (request.Status is TicketStatus.WaitingForCustomer or TicketStatus.WaitingForMoreInformation)
                {
                    var latestAdminMessage = ticket.Messages
                        .Where(message => message.SenderId != ticket.CreatorId)
                        .OrderByDescending(message => message.CreatedAt)
                        .FirstOrDefault();

                    await SendTicketCreatorEmailAsync(
                        ticket,
                        localizationService.Get("ticketStatusUpdatedTitle", ticket.Creator.Language),
                        localizationService.Get("ticketStatusUpdatedBody", ticket.Creator.Language, new
                        {
                            ticketTitle = ticket.Title,
                            oldStatus = oldStatus.ToString(),
                            newStatus = request.Status.ToString(),
                            updaterName = currentUser!.Nick
                        }),
                        latestAdminMessage?.Content
                    );
                }
            }

            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_STATUS_UPDATE_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize]
    [AskPermission(PermissionKeys.TicketsAssign)]
    [ProducesResponseType<SnTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SnTicket>> Assign(Guid id, [FromBody] AssignRequest request)
    {
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (!isAdmin) return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to assign tickets.", forbidden: true));

        try
        {
            var existingTicket = await ticketService.GetTicketByIdAsync(id);
            if (existingTicket == null) return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

            var oldAssignee = existingTicket.Assignee;
            var ticket = await ticketService.AssignAsync(id, request.AssigneeId);

            if (request.AssigneeId.HasValue && ticket.Assignee != null)
            {
                await NotifyTicketAssignedAsync(ticket, oldAssignee, ticket.Assignee, currentUser!);
            }

            return Ok(ticket);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "PASSPORT_TICKET_NOT_FOUND", Message = "Ticket not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_TICKET_ASSIGN_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
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
        var (isAdmin, currentUser) = await GetCurrentUserAsync();
        if (currentUser == null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (!isAdmin && !IsSelfScopedTicketQuery(currentUser, creatorId, assigneeId))
            return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized("You do not have permission to view these tickets.", forbidden: true));

        var count = await ticketService.CountTicketsAsync(creatorId, assigneeId, status);
        return Ok(new { count });
    }
}
