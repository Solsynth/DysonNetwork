using System.Globalization;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Email;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Pages.Emails;
using DysonNetwork.Sphere.Storage;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using Org.BouncyCastle.Utilities;
using OtpNet;

namespace DysonNetwork.Sphere.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    NotificationService nty,
    EmailService email,
    IStringLocalizer<NotificationResource> localizer,
    ICacheService cache,
    ILogger<AccountService> logger
)
{
    public static void SetCultureInfo(Account account)
    {
        SetCultureInfo(account.Language);
    }

    public static void SetCultureInfo(string? languageCode)
    {
        var info = new CultureInfo(languageCode ?? "en-us", false);
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;
    }

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
        return contact?.Account;
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
                    Type = AccountAuthFactorType.TimedCode,
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
        factor.AccountId = account.Id;
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

    public async Task<AccountAuthFactor> DisableAuthFactor(AccountAuthFactor factor)
    {
        if (factor.EnabledAt is null) throw new ArgumentException("The factor has been disabled.");

        var count = await db.AccountAuthFactors
            .Where(f => f.AccountId == factor.AccountId && f.EnabledAt != null)
            .CountAsync();
        if (count <= 1)
            throw new InvalidOperationException(
                "Disabling this auth factor will cause you have no active auth factors.");

        factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(factor);
        await db.SaveChangesAsync();
        
        return factor;
    }

    public async Task DeleteAuthFactor(AccountAuthFactor factor)
    {
        var count = await db.AccountAuthFactors
            .Where(f => f.AccountId == factor.AccountId)
            .If(factor.EnabledAt is not null, q => q.Where(f => f.EnabledAt != null))
            .CountAsync();
        if (count <= 1)
            throw new InvalidOperationException("Deleting this auth factor will cause you have no auth factor.");

        db.AccountAuthFactors.Remove(factor);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Send the auth factor verification code to users, for factors like in-app code and email.
    /// Sometimes it requires a hint, like a part of the user's email address to ensure the user is who own the account.
    /// </summary>
    /// <param name="account">The owner of the auth factor</param>
    /// <param name="factor">The auth factor needed to send code</param>
    /// <param name="hint">The part of the contact method for verification</param>
    public async Task SendFactorCode(Account account, AccountAuthFactor factor, string? hint = null)
    {
        var code = new Random().Next(100000, 999999).ToString("000000");

        switch (factor.Type)
        {
            case AccountAuthFactorType.InAppCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");
                
                await nty.SendNotification(
                    account,
                    "auth.verification",
                    localizer["AuthCodeTitle"],
                    null,
                    localizer["AuthCodeBody", code],
                    save: true
                );
                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(5));
                break;
            case AccountAuthFactorType.EmailCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");
                
                ArgumentNullException.ThrowIfNull(hint);
                hint = hint.Replace("@", "").Replace(".", "").Replace("+", "").Replace("%", "");
                if (string.IsNullOrWhiteSpace(hint))
                {
                    logger.LogWarning(
                        "Unable to send factor code to #{FactorId} with hint {Hint}, due to invalid hint...",
                        factor.Id,
                        hint
                    );
                    return;
                }

                var contact = await db.AccountContacts
                    .Where(c => c.Type == AccountContactType.Email)
                    .Where(c => c.VerifiedAt != null)
                    .Where(c => EF.Functions.ILike(c.Content, $"%{hint}%"))
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync();
                if (contact is null)
                {
                    logger.LogWarning(
                        "Unable to send factor code to #{FactorId} with hint {Hint}, due to no contact method found according to hint...",
                        factor.Id,
                        hint
                    );
                    return;
                }

                await email.SendTemplatedEmailAsync<VerificationEmail, VerificationEmailModel>(
                    contact.Content,
                    localizer["EmailVerificationTitle"],
                    localizer["VerificationEmail"],
                    new VerificationEmailModel
                    {
                        Name = account.Name,
                        Code = code
                    }
                );

                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(30));
                break;
            case AccountAuthFactorType.Password:
            case AccountAuthFactorType.TimedCode:
            default:
                // No need to send, such as password etc...
                return;
        }
    }

    public async Task<bool> VerifyFactorCode(AccountAuthFactor factor, string code)
    {
        switch (factor.Type)
        {
            case AccountAuthFactorType.EmailCode:
            case AccountAuthFactorType.InAppCode:
                var correctCode = await _GetFactorCode(factor);
                return correctCode is not null && string.Equals(correctCode, code, StringComparison.OrdinalIgnoreCase);
            case AccountAuthFactorType.Password:
            case AccountAuthFactorType.TimedCode:
            default:
                return factor.VerifyPassword(code);
        }
    }

    private const string AuthFactorCachePrefix = "authfactor:";

    private async Task _SetFactorCode(AccountAuthFactor factor, string code, TimeSpan expires)
    {
        await cache.SetAsync(
            $"{AuthFactorCachePrefix}{factor.Id}:code",
            code,
            expires
        );
    }

    private async Task<string?> _GetFactorCode(AccountAuthFactor factor)
    {
        return await cache.GetAsync<string?>(
            $"{AuthFactorCachePrefix}{factor.Id}:code"
        );
    }

    public async Task DeleteSession(Account account, Guid sessionId)
    {
        var session = await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.Id == sessionId && s.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (session is null) throw new InvalidOperationException("Session was not found.");

        var sessions = await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.AccountId == session.Id && s.Challenge.DeviceId == session.Challenge.DeviceId)
            .ToListAsync();

        if (session.Challenge.DeviceId is not null)
            await nty.UnsubscribePushNotifications(session.Challenge.DeviceId);

        // The current session should be included in the sessions' list
        db.AuthSessions.RemoveRange(sessions);
        await db.SaveChangesAsync();

        foreach (var item in sessions)
            await cache.RemoveAsync($"{DysonTokenAuthHandler.AuthCachePrefix}{item.Id}");
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