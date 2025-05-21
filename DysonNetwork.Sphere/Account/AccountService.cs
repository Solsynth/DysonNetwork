using System.Globalization;
using System.Reflection;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Permission;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace DysonNetwork.Sphere.Account;

public class AccountService(
    AppDatabase db,
    IMemoryCache cache,
    IStringLocalizerFactory factory
)
{
    public async Task PurgeAccountCache(Account account)
    {
        cache.Remove($"UserFriends_{account.Id}");

        var sessions = await db.AuthSessions.Where(e => e.Account.Id == account.Id).Select(e => e.Id)
            .ToListAsync();
        foreach (var session in sessions)
        {
            cache.Remove($"Auth_{session}");
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

    public async Task<int?> GetAccountLevel(Guid accountId)
    {
        var profile = await db.AccountProfiles
            .Where(a => a.AccountId == accountId)
            .FirstOrDefaultAsync();
        return profile?.Level;
    }

    /// Maintenance methods for server administrator
    public async Task EnsureAccountProfileCreated()
    {
        var accountsId = await db.Accounts.Select(a => a.Id).ToListAsync();
        var missingId = await db.AccountProfiles
            .IgnoreAutoIncludes()
            .Where(p => !accountsId.Contains(p.AccountId))
            .Select(p => p.AccountId)
            .ToListAsync();

        if (missingId.Count != 0)
        {
            var newProfiles = missingId.Select(id => new Profile { AccountId = id }).ToList();
            await db.BulkInsertAsync(newProfiles, config => config.ConflictOption = ConflictOption.Ignore);
        }
    }
}