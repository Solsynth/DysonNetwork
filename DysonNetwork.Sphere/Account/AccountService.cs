using DysonNetwork.Sphere.Storage;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    ICacheService cache
)
{
    public const string AccountCachePrefix = "Account_";

    public async Task PurgeAccountCache(Account account)
    {
        await cache.RemoveGroupAsync($"{AccountCachePrefix}{account.Id}");
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

    public async Task RequestAccountDeletion(Account account)
    {
        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.AccountRemoval,
            new Dictionary<string, object>(),
            SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)),
            preventRepeat: true
        );
        await spells.NotifyMagicSpell(spell);
    }

    public async Task RequestPasswordReset(Account account)
    {
        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.AuthPasswordReset,
            new Dictionary<string, object>(),
            SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)),
            preventRepeat: true
        );
        await spells.NotifyMagicSpell(spell);
    }

    /// Maintenance methods for server administrator
    public async Task EnsureAccountProfileCreated()
    {
        var accountsId = await db.Accounts.Select(a => a.Id).ToListAsync();
        var existingId = await db.AccountProfiles.Select(p => p.AccountId).ToListAsync();
        var missingId = accountsId.Except(existingId).ToList();

        if (missingId.Count != 0)
        {
            var newProfiles = missingId.Select(id => new Profile { Id = Guid.NewGuid(), AccountId = id }).ToList();
            await db.BulkInsertAsync(newProfiles);
        }
    }
}