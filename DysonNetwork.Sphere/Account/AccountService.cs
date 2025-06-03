using DysonNetwork.Sphere.Storage;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using OtpNet;

namespace DysonNetwork.Sphere.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    ICacheService cache
)
{
    public const string AccountCachePrefix = "account:";

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

    public async Task<bool> CheckAuthFactorExists(Account account, AccountAuthFactorType type)
    {
        var isExists = await db.AccountAuthFactors
            .Where(x => x.AccountId == account.Id && x.Type == type)
            .AnyAsync();
        return isExists;
    }

    public async Task<AccountAuthFactor?> CreateAuthFactor(Account account, AccountAuthFactorType type, string? secret)
    {
        AccountAuthFactor? factor = null;
        switch (type)
        {
            case AccountAuthFactorType.Password:
                if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentNullException(nameof(secret));
                factor = new AccountAuthFactor
                {
                    Type = AccountAuthFactorType.Password,
                    Trustworthy = 1,
                    AccountId = account.Id,
                    Secret = secret,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                }.HashSecret();
                break;
            case AccountAuthFactorType.EmailCode:
                factor = new AccountAuthFactor
                {
                    Type = AccountAuthFactorType.EmailCode,
                    Trustworthy = 2,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                };
                break;
            case AccountAuthFactorType.InAppCode:
                factor = new AccountAuthFactor
                {
                    Type = AccountAuthFactorType.InAppCode,
                    Trustworthy = 1,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant()
                };
                break;
            case AccountAuthFactorType.TimedCode:
                var skOtp = KeyGeneration.GenerateRandomKey(20);
                var skOtp32 = Base32Encoding.ToString(skOtp);
                factor = new AccountAuthFactor
                {
                    Secret = skOtp32,
                    Type = AccountAuthFactorType.InAppCode,
                    Trustworthy = 2,
                    EnabledAt = null, // It needs to be tired once to enable
                    CreatedResponse = new Dictionary<string, object>
                    {
                        ["uri"] = new OtpUri(
                            OtpType.Totp,
                            skOtp32,
                            account.Id.ToString(),
                            "Solar Network"
                        ).ToString(),
                    }
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        if (factor is null) throw new InvalidOperationException("Unable to create auth factor.");
        db.AccountAuthFactors.Add(factor);
        await db.SaveChangesAsync();
        return factor;
    }

    public async Task<AccountAuthFactor> EnableAuthFactor(AccountAuthFactor factor, string code)
    {
        if (factor.EnabledAt is not null) throw new ArgumentException("The factor has been enabled.");
        if (!factor.VerifyPassword(code))
            throw new InvalidOperationException(
                "Invalid code, you need to enter the correct code to enable the factor."
            );
        
        factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(factor);
        await db.SaveChangesAsync();

        return factor;
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