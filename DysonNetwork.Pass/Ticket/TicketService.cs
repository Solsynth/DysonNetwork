using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AccountService = DysonNetwork.Pass.Account.AccountService;

namespace DysonNetwork.Pass.Ticket;

public class TicketService(
    AppDatabase db,
    FileService.FileServiceClient files,
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

        var fileRequestDataRequest = new GetFileBatchRequest();
        fileRequestDataRequest.Ids.AddRange(fileIds ?? []);
        var fileData = await files.GetFileBatchAsync(fileRequestDataRequest);
        var fileDataParsed = fileData.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();

        var ticket = new SnTicket
        {
            Title = title,
            Type = type,
            Priority = priority,
            CreatorId = creatorId,
            Creator = creator,
            Status = TicketStatus.Open,
            Messages =
            [
                new SnTicketMessage
                {
                    Content = content,
                    Files = fileDataParsed,
                }
            ]
        };

        db.Tickets.Add(ticket);

        await db.SaveChangesAsync();

        logger.LogInformation("Ticket created: {TicketId} by {CreatorId}", ticket.Id, creatorId);

        return ticket;
    }

    public async Task<SnTicket?> GetTicketByIdAsync(Guid id)
    {
        return await db.Tickets
            .Where(t => t.Id == id)
            .Include(t => t.Creator)
            .Include(t => t.Assignee)
            .Include(t => t.Messages)
            .ThenInclude(m => m.Sender)
            .FirstOrDefaultAsync();
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
            .Include(t => t.Creator)
            .Include(t => t.Assignee)
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

        return await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
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

        var fileRequestDataRequest = new GetFileBatchRequest();
        fileRequestDataRequest.Ids.AddRange(fileIds ?? []);
        var fileData = await files.GetFileBatchAsync(fileRequestDataRequest);
        var fileDataParsed = fileData.Files.Select(SnCloudFileReferenceObject.FromProtoValue).ToList();

        var message = new SnTicketMessage
        {
            TicketId = ticketId,
            SenderId = senderId,
            Sender = sender,
            Content = content,
            Files = fileDataParsed,
        };

        db.TicketMessages.Add(message);
        await db.SaveChangesAsync();

        logger.LogInformation("Message added to ticket {TicketId} by {SenderId}", ticketId, senderId);

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
}