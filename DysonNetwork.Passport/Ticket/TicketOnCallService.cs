using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using AccountService = DysonNetwork.Passport.Account.AccountService;

namespace DysonNetwork.Passport.Ticket;

public class TicketOnCallService(
    AppDatabase db,
    AccountService accounts,
    ILogger<TicketOnCallService> logger
)
{
    public async Task<List<SnTicketOnCallAdmin>> GetOnCallRosterAsync()
    {
        var roster = await db.TicketOnCallAdmins
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        foreach (var entry in roster)
            entry.Account = await accounts.GetAccount(entry.AccountId);

        return roster;
    }

    public async Task<SnTicketOnCallAdmin> AddOnCallAdminAsync(Guid accountId)
    {
        _ = await accounts.GetAccount(accountId)
            ?? throw new InvalidOperationException("Account not found");

        var existing = await db.TicketOnCallAdmins
            .FirstOrDefaultAsync(x => x.AccountId == accountId);
        if (existing is not null) return existing;

        var entry = new SnTicketOnCallAdmin { AccountId = accountId };
        db.TicketOnCallAdmins.Add(entry);
        await db.SaveChangesAsync();

        logger.LogInformation("Ticket on-call admin added: {AccountId}", accountId);
        return entry;
    }

    public async Task<bool> RemoveOnCallAdminAsync(Guid accountId)
    {
        var removed = await db.TicketOnCallAdmins
            .Where(x => x.AccountId == accountId)
            .ExecuteDeleteAsync();
        if (removed > 0)
            logger.LogInformation("Ticket on-call admin removed: {AccountId}", accountId);
        return removed > 0;
    }
}
