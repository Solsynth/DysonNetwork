using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

public class AccountService(AppDatabase db)
{
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