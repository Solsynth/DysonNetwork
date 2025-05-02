using DysonNetwork.Sphere.Permission;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DysonNetwork.Sphere.Account;

public class AccountService(AppDatabase db, PermissionService pm, IMemoryCache cache)
{
    public async Task PurgeAccountCache(Account account)
    {
        cache.Remove($"dyn_user_friends_{account.Id}");
        
        var sessions = await db.AuthSessions.Where(e => e.Account.Id == account.Id).Select(e => e.Id)
            .ToListAsync();
        foreach (var session in sessions)
        {
            cache.Remove($"dyn_auth_{session}");
        }
    }

    public async Task<Account?> LookupAccount(string probe)
    {
        var account = await db.Accounts.Where(a => a.Name == probe).FirstOrDefaultAsync();
        if (account is not null) return account;

        var contact = await db.AccountContacts
            .Where(c => c.Content == probe)
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        if (contact is not null) return contact.Account;

        return null;
    }
}