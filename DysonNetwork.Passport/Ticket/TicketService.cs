using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AccountService = DysonNetwork.Passport.Account.AccountService;

namespace DysonNetwork.Passport.Ticket;

public class TicketService(
    AppDatabase db,
    DyFileService.DyFileServiceClient files,
    AccountService accounts,
    ILogger<TicketService> logger
)
{
    public async Task<SnTicket> CreateTicketAsync(
        string title,
        string content,
        TicketType type,
        TicketPriority priority,
        Guid creatorId,
        List<string>? fileIds = null
    )
    {
        var creator = await accounts.GetAccount(creatorId)
                      ?? throw new InvalidOperationException("Creator not found");

        var fileRequestDataRequest = new DyGetFileBatchRequest();
        fileRequestDataRequest.Ids.AddRange(fileIds ?? []);
        var fileData = await files.GetFileBatchAsync(fileRequestDataRequest);
        var fileDataParsed = fileData.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();

        var ticket = new SnTicket
        {
            Title = title,
            Type = type,
            Priority = priority,
            CreatorId = creatorId,
            Status = TicketStatus.Open,
            Messages =
            [
                new SnTicketMessage
                {
                    Content = content,
                    Files = fileDataParsed,
                    SenderId = creatorId,
                }
            ]
        };

        db.Tickets.Add(ticket);

        await db.SaveChangesAsync();

        logger.LogInformation("Ticket created: {TicketId} by {CreatorId}", ticket.Id, creatorId);

        await HydrateTicketAccountsAsync(ticket);
        return ticket;
    }

    public async Task<SnTicket?> GetTicketByIdAsync(Guid id)
    {
        var ticket = await db.Tickets
            .Where(t => t.Id == id)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync();
        if (ticket is null) return null;
        await HydrateTicketAccountsAsync(ticket);
        return ticket;
    }

    public async Task<List<SnTicket>> GetTicketsAsync(
        Guid? creatorId = null,
        Guid? assigneeId = null,
        TicketType? type = null,
        TicketStatus? status = null,
        TicketPriority? priority = null,
        int offset = 0,
        int take = 20
    )
    {
        var query = db.Tickets
            .Include(t => t.Messages)
            .AsQueryable();

        if (creatorId.HasValue)
            query = query.Where(t => t.CreatorId == creatorId.Value);

        if (assigneeId.HasValue)
            query = query.Where(t => t.AssigneeId == assigneeId.Value);

        if (type.HasValue)
            query = query.Where(t => t.Type == type.Value);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        var tickets = await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        if (tickets.Count == 0) return tickets;
        foreach (var ticket in tickets) await HydrateTicketAccountsAsync(ticket);
        return tickets;
    }

    public async Task<SnTicketMessage> AddMessageAsync(
        Guid ticketId,
        Guid senderId,
        string content,
        List<string>? fileIds
    )
    {
        _ = await db.Tickets.FindAsync(ticketId)
            ?? throw new KeyNotFoundException("Ticket not found");

        var sender = await accounts.GetAccount(senderId)
                     ?? throw new InvalidOperationException("Sender not found");

        var fileRequestDataRequest = new DyGetFileBatchRequest();
        fileRequestDataRequest.Ids.AddRange(fileIds ?? []);
        var fileData = await files.GetFileBatchAsync(fileRequestDataRequest);
        var fileDataParsed = fileData.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();

        var message = new SnTicketMessage
        {
            TicketId = ticketId,
            SenderId = senderId,
            Content = content,
            Files = fileDataParsed,
        };

        db.TicketMessages.Add(message);
        await db.SaveChangesAsync();

        logger.LogInformation("Message added to ticket {TicketId} by {SenderId}", ticketId, senderId);

        message.Sender = sender;
        return message;
    }

    public async Task<SnTicket> UpdateStatusAsync(Guid ticketId, TicketStatus newStatus)
    {
        var ticket = await db.Tickets.FindAsync(ticketId)
                     ?? throw new KeyNotFoundException("Ticket not found");

        ticket.Status = newStatus;

        if (newStatus == TicketStatus.Resolved || newStatus == TicketStatus.Closed)
        {
            ticket.ResolvedAt = SystemClock.Instance.GetCurrentInstant();
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Ticket {TicketId} status updated to {Status}", ticketId, newStatus);

        return ticket;
    }

    public async Task<SnTicket> AssignAsync(Guid ticketId, Guid? assigneeId)
    {
        var ticket = await db.Tickets.FindAsync(ticketId)
                     ?? throw new KeyNotFoundException("Ticket not found");

        if (assigneeId.HasValue)
        {
            var assignee = await accounts.GetAccount(assigneeId.Value)
                           ?? throw new InvalidOperationException("Assignee not found");
            ticket.Assignee = assignee;
        }

        ticket.AssigneeId = assigneeId;

        await db.SaveChangesAsync();

        logger.LogInformation("Ticket {TicketId} assigned to {AssigneeId}", ticketId, assigneeId);

        return ticket;
    }

    public async Task<SnTicket> UpdateAsync(
        Guid ticketId,
        string? title = null,
        TicketType? type = null,
        TicketPriority? priority = null
    )
    {
        var ticket = await db.Tickets.FindAsync(ticketId)
                     ?? throw new KeyNotFoundException("Ticket not found");

        if (title != null) ticket.Title = title;
        if (type.HasValue) ticket.Type = type.Value;
        if (priority.HasValue) ticket.Priority = priority.Value;

        await db.SaveChangesAsync();

        logger.LogInformation("Ticket {TicketId} updated", ticketId);

        return ticket;
    }

    public async Task DeleteTicketAsync(Guid ticketId)
    {
        var ticket = await db.Tickets.FindAsync(ticketId)
                     ?? throw new KeyNotFoundException("Ticket not found");

        db.Tickets.Remove(ticket);
        await db.SaveChangesAsync();

        logger.LogInformation("Ticket {TicketId} deleted", ticketId);
    }

    public async Task<int> CountTicketsAsync(
        Guid? creatorId = null,
        Guid? assigneeId = null,
        TicketStatus? status = null
    )
    {
        var query = db.Tickets.AsQueryable();

        if (creatorId.HasValue)
            query = query.Where(t => t.CreatorId == creatorId.Value);

        if (assigneeId.HasValue)
            query = query.Where(t => t.AssigneeId == assigneeId.Value);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        return await query.CountAsync();
    }

    private async Task HydrateTicketAccountsAsync(SnTicket ticket)
    {
        ticket.Creator = await accounts.GetAccount(ticket.CreatorId)
            ?? throw new InvalidOperationException($"Creator {ticket.CreatorId} not found");

        ticket.Assignee = ticket.AssigneeId.HasValue
            ? await accounts.GetAccount(ticket.AssigneeId.Value)
            : null;

        if (ticket.Messages.Count == 0) return;
        foreach (var message in ticket.Messages)
        {
            message.Sender = await accounts.GetAccount(message.SenderId)
                ?? throw new InvalidOperationException($"Sender {message.SenderId} not found");
        }
    }
}
