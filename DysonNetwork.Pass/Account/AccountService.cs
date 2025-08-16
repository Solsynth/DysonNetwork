using System.Globalization;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Auth.OpenId;
using DysonNetwork.Pass.Email;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using OtpNet;

namespace DysonNetwork.Pass.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    AccountUsernameService uname,
    EmailService mailer,
    PusherService.PusherServiceClient pusher,
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

    public async Task<Account?> LookupAccountByConnection(string identifier, string provider)
    {
        var connection = await db.AccountConnections
            .Where(c => c.ProvidedIdentifier == identifier && c.Provider == provider)
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        return connection?.Account;
    }

    public async Task<int?> GetAccountLevel(Guid accountId)
    {
        var profile = await db.AccountProfiles
            .Where(a => a.AccountId == accountId)
            .FirstOrDefaultAsync();
        return profile?.Level;
    }

    public async Task<Account> CreateAccount(
        string name,
        string nick,
        string email,
        string? password,
        string language = "en-US",
        bool isEmailVerified = false,
        bool isActivated = false
    )
    {
        var dupeNameCount = await db.Accounts.Where(a => a.Name == name).CountAsync();
        if (dupeNameCount > 0)
            throw new InvalidOperationException("Account name has already been taken.");

        var dupeEmailCount = await db.AccountContacts
            .Where(c => c.Content == email && c.Type == AccountContactType.Email
            ).CountAsync();
        if (dupeEmailCount > 0)
            throw new InvalidOperationException("Account email has already been used.");

        var account = new Account
        {
            Name = name,
            Nick = nick,
            Language = language,
            Contacts = new List<AccountContact>
            {
                new()
                {
                    Type = AccountContactType.Email,
                    Content = email,
                    VerifiedAt = isEmailVerified ? SystemClock.Instance.GetCurrentInstant() : null,
                    IsPrimary = true
                }
            },
            AuthFactors = password is not null
                ? new List<AccountAuthFactor>
                {
                    new AccountAuthFactor
                    {
                        Type = AccountAuthFactorType.Password,
                        Secret = password,
                        EnabledAt = SystemClock.Instance.GetCurrentInstant()
                    }.HashSecret()
                }
                : [],
            Profile = new AccountProfile()
        };

        if (isActivated)
        {
            account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
            var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
            if (defaultGroup is not null)
            {
                db.PermissionGroupMembers.Add(new PermissionGroupMember
                {
                    Actor = $"user:{account.Id}",
                    Group = defaultGroup
                });
            }
        }

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        if (isActivated) return account;

        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.AccountActivation,
            new Dictionary<string, object>
            {
                { "contact_method", account.Contacts.First().Content }
            }
        );
        await spells.NotifyMagicSpell(spell, true);

        return account;
    }

    public async Task<Account> CreateAccount(OidcUserInfo userInfo)
    {
        if (string.IsNullOrEmpty(userInfo.Email))
            throw new ArgumentException("Email is required for account creation");

        var displayName = !string.IsNullOrEmpty(userInfo.DisplayName)
            ? userInfo.DisplayName
            : $"{userInfo.FirstName} {userInfo.LastName}".Trim();

        // Generate username from email
        var username = await uname.GenerateUsernameFromEmailAsync(userInfo.Email);

        return await CreateAccount(
            username,
            displayName,
            userInfo.Email,
            null,
            "en-US",
            userInfo.EmailVerified,
            userInfo.EmailVerified
        );
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
            case AccountAuthFactorType.PinCode:
                if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentNullException(nameof(secret));
                if (!secret.All(char.IsDigit) || secret.Length != 6)
                    throw new ArgumentException("PIN code must be exactly 6 digits");
                factor = new AccountAuthFactor
                {
                    Type = AccountAuthFactorType.PinCode,
                    Trustworthy = 0, // Only for confirming, can't be used for login
                    Secret = secret,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                }.HashSecret();
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

    public async Task<AccountAuthFactor> EnableAuthFactor(AccountAuthFactor factor, string? code)
    {
        if (factor.EnabledAt is not null) throw new ArgumentException("The factor has been enabled.");
        if (factor.Type is AccountAuthFactorType.Password or AccountAuthFactorType.TimedCode)
        {
            if (code is null || !factor.VerifyPassword(code))
                throw new InvalidOperationException(
                    "Invalid code, you need to enter the correct code to enable the factor."
                );
        }

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

        factor.EnabledAt = null;
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

                await pusher.SendPushNotificationToUserAsync(
                    new SendPushNotificationToUserRequest
                    {
                        UserId = account.Id.ToString(),
                        Notification = new PushNotification
                        {
                            Topic = "auth.verification",
                            Title = localizer["AuthCodeTitle"],
                            Body = localizer["AuthCodeBody", code],
                            IsSavable = false
                        }
                    }
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

                await mailer
                    .SendTemplatedEmailAsync<Pages.Emails.VerificationEmail, VerificationEmailModel>(
                        account.Nick,
                        contact.Content,
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
                var isCorrect = correctCode is not null &&
                                string.Equals(correctCode, code, StringComparison.OrdinalIgnoreCase);
                await cache.RemoveAsync($"{AuthFactorCachePrefix}{factor.Id}:code");
                return isCorrect;
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

    public async Task<bool> IsDeviceActive(Guid id)
    {
        return await db.AuthSessions
            .Include(s => s.Challenge)
            .AnyAsync(s => s.Challenge.ClientId == id);
    }

    public async Task<AuthClient> UpdateDeviceName(Account account, string deviceId, string label)
    {
        var device = await db.AuthClients.FirstOrDefaultAsync(
            c => c.DeviceId == deviceId && c.AccountId == account.Id
        );
        if (device is null) throw new InvalidOperationException("Device was not found.");

        device.DeviceLabel = label;
        db.Update(device);
        await db.SaveChangesAsync();

        return device;
    }

    public async Task DeleteSession(Account account, Guid sessionId)
    {
        var session = await db.AuthSessions
            .Include(s => s.Challenge)
            .ThenInclude(s => s.Client)
            .Where(s => s.Id == sessionId && s.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (session is null) throw new InvalidOperationException("Session was not found.");

        var sessions = await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.AccountId == session.Id && s.Challenge.DeviceId == session.Challenge.DeviceId)
            .ToListAsync();

        if (session.Challenge.ClientId.HasValue)
        {
            if (!await IsDeviceActive(session.Challenge.ClientId.Value))
                await pusher.UnsubscribePushNotificationsAsync(new UnsubscribePushNotificationsRequest()
                { DeviceId = session.Challenge.Client!.DeviceId }
                );
        }

        // The current session should be included in the sessions' list
        await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.Challenge.DeviceId == session.Challenge.DeviceId)
            .ExecuteDeleteAsync();

        foreach (var item in sessions)
            await cache.RemoveAsync($"{DysonTokenAuthHandler.AuthCachePrefix}{item.Id}");
    }

    public async Task DeleteDevice(Account account, string deviceId)
    {
        var device = await db.AuthClients.FirstOrDefaultAsync(
            c => c.DeviceId == deviceId && c.AccountId == account.Id
        );
        if (device is null)
            throw new InvalidOperationException("Device not found.");

        await pusher.UnsubscribePushNotificationsAsync(
            new UnsubscribePushNotificationsRequest() { DeviceId = device.DeviceId }
        );

        db.AuthClients.Remove(device);
        await db.SaveChangesAsync();

        var sessions = await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.Challenge.ClientId == device.Id)
            .ToListAsync();

        // The current session should be included in the sessions' list
        await db.AuthSessions
            .Include(s => s.Challenge)
            .Where(s => s.Challenge.DeviceId == device.DeviceId)
            .ExecuteDeleteAsync();

        foreach (var item in sessions)
            await cache.RemoveAsync($"{DysonTokenAuthHandler.AuthCachePrefix}{item.Id}");
    }

    public async Task<AccountContact> CreateContactMethod(Account account, AccountContactType type, string content)
    {
        var isExists = await db.AccountContacts
            .Where(x => x.AccountId == account.Id && x.Type == type && x.Content == content)
            .AnyAsync();
        if (isExists)
            throw new InvalidOperationException("Contact method already exists.");

        var contact = new AccountContact
        {
            Type = type,
            Content = content,
            AccountId = account.Id,
        };

        db.AccountContacts.Add(contact);
        await db.SaveChangesAsync();

        return contact;
    }

    public async Task VerifyContactMethod(Account account, AccountContact contact)
    {
        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.ContactVerification,
            new Dictionary<string, object> { { "contact_method", contact.Content } },
            expiredAt: SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)),
            preventRepeat: true
        );
        await spells.NotifyMagicSpell(spell);
    }

    public async Task<AccountContact> SetContactMethodPrimary(Account account, AccountContact contact)
    {
        if (contact.AccountId != account.Id)
            throw new InvalidOperationException("Contact method does not belong to this account.");
        if (contact.VerifiedAt is null)
            throw new InvalidOperationException("Cannot set unverified contact method as primary.");

        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            await db.AccountContacts
                .Where(c => c.AccountId == account.Id && c.Type == contact.Type)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPrimary, false));

            contact.IsPrimary = true;
            db.AccountContacts.Update(contact);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
            return contact;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<AccountContact> SetContactMethodPublic(Account account, AccountContact contact, bool isPublic)
    {
        contact.IsPublic = isPublic;
        db.AccountContacts.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task DeleteContactMethod(Account account, AccountContact contact)
    {
        if (contact.AccountId != account.Id)
            throw new InvalidOperationException("Contact method does not belong to this account.");
        if (contact.IsPrimary)
            throw new InvalidOperationException("Cannot delete primary contact method.");

        db.AccountContacts.Remove(contact);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// This method will grant a badge to the account.
    /// Shouldn't be exposed to normal user and the user itself.
    /// </summary>
    public async Task<AccountBadge> GrantBadge(Account account, AccountBadge badge)
    {
        badge.AccountId = account.Id;
        db.Badges.Add(badge);
        await db.SaveChangesAsync();
        return badge;
    }

    /// <summary>
    /// This method will revoke a badge from the account.
    /// Shouldn't be exposed to normal user and the user itself.
    /// </summary>
    public async Task RevokeBadge(Account account, Guid badgeId)
    {
        var badge = await db.Badges
            .Where(b => b.AccountId == account.Id && b.Id == badgeId)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
        if (badge is null) throw new InvalidOperationException("Badge was not found.");

        var profile = await db.AccountProfiles
            .Where(p => p.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (profile?.ActiveBadge is not null && profile.ActiveBadge.Id == badge.Id)
            profile.ActiveBadge = null;

        db.Remove(badge);
        await db.SaveChangesAsync();
    }

    public async Task ActiveBadge(Account account, Guid badgeId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var badge = await db.Badges
                .Where(b => b.AccountId == account.Id && b.Id == badgeId)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
            if (badge is null) throw new InvalidOperationException("Badge was not found.");

            await db.Badges
                .Where(b => b.AccountId == account.Id && b.Id != badgeId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActivatedAt, p => null));

            badge.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(badge);
            await db.SaveChangesAsync();

            await db.AccountProfiles
                .Where(p => p.AccountId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActiveBadge, badge.ToReference()));
            await PurgeAccountCache(account);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// The maintenance method for server administrator.
    /// To check every user has an account profile and to create them if it isn't having one.
    /// </summary>
    public async Task EnsureAccountProfileCreated()
    {
        var accountsId = await db.Accounts.Select(a => a.Id).ToListAsync();
        var existingId = await db.AccountProfiles.Select(p => p.AccountId).ToListAsync();
        var missingId = accountsId.Except(existingId).ToList();

        if (missingId.Count != 0)
        {
            var newProfiles = missingId.Select(id => new AccountProfile { Id = Guid.NewGuid(), AccountId = id })
                .ToList();
            await db.BulkInsertAsync(newProfiles);
        }
    }
}
