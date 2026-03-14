using DysonNetwork.Padlock.Auth.OpenId;
using DysonNetwork.Padlock.Mailer;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Account;

public class AccountService(
    AppDatabase db,
    ICacheService cache,
    IEventBus eventBus,
    DyFileService.DyFileServiceClient files,
    DyRingService.DyRingServiceClient ring,
    EmailService mailer,
    ILocalizationService localizer,
    ILogger<AccountService> logger
)
{
    private const string AuthFactorCachePrefix = "authfactor:";

    public async Task<SnAccount?> LookupAccount(string probe)
    {
        var account = await db.Accounts.Where(a => EF.Functions.ILike(a.Name, probe)).FirstOrDefaultAsync();
        if (account is not null) return account;

        var contact = await db.AccountContacts
            .Where(c => c.Type == AccountContactType.Email || c.Type == AccountContactType.PhoneNumber)
            .Where(c => EF.Functions.ILike(c.Content, probe))
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        return contact?.Account;
    }

    public async Task<bool> CheckAccountNameHasTaken(string name)
    {
        return await db.Accounts.AnyAsync(a => EF.Functions.ILike(a.Name, name));
    }

    public async Task<bool> CheckEmailHasBeenUsed(string email)
    {
        return await db.AccountContacts.AnyAsync(c =>
            c.Type == AccountContactType.Email && EF.Functions.ILike(c.Content, email));
    }

    public async Task<SnAccount> CreateAccount(
        string name,
        string nick,
        string email,
        string? password,
        string language = "en-US",
        string region = "en",
        bool isEmailVerified = false,
        bool isActivated = false
    )
    {
        if (await CheckAccountNameHasTaken(name))
            throw new InvalidOperationException("Account name has already been taken.");
        if (await CheckEmailHasBeenUsed(email))
            throw new InvalidOperationException("Account email has already been used.");

        var account = new SnAccount
        {
            Name = name,
            Nick = nick,
            Language = language,
            Region = region,
            Contacts =
            [
                new SnAccountContact
                {
                    Type = AccountContactType.Email,
                    Content = email,
                    VerifiedAt = isEmailVerified ? SystemClock.Instance.GetCurrentInstant() : null,
                    IsPrimary = true
                }
            ],
            AuthFactors = password is not null
                ?
                [
                    new SnAccountAuthFactor
                    {
                        Type = AccountAuthFactorType.Password,
                        Secret = password,
                        EnabledAt = SystemClock.Instance.GetCurrentInstant()
                    }.HashSecret()
                ]
                : [],
            ActivatedAt = isActivated ? SystemClock.Instance.GetCurrentInstant() : null
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        await PublishAccountCreated(account);
        return account;
    }

    public async Task<SnAccount> CreateAccount(OidcUserInfo userInfo)
    {
        if (string.IsNullOrWhiteSpace(userInfo.Email))
            throw new ArgumentException("Email is required for account creation");

        var displayName = !string.IsNullOrWhiteSpace(userInfo.DisplayName)
            ? userInfo.DisplayName
            : $"{userInfo.FirstName} {userInfo.LastName}".Trim();
        var baseName = userInfo.Email.Split('@')[0].ToLowerInvariant();
        var name = await GenerateAvailableUsername(baseName);

        return await CreateAccount(
            name,
            string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            userInfo.Email,
            null,
            isEmailVerified: userInfo.EmailVerified
        );
    }

    public async Task<bool> CheckAuthFactorExists(SnAccount account, AccountAuthFactorType type)
    {
        return await db.AccountAuthFactors
            .Where(x => x.AccountId == account.Id && x.Type == type)
            .AnyAsync();
    }

    public async Task<SnAccountAuthFactor?> CreateAuthFactor(SnAccount account, AccountAuthFactorType type, string? secret)
    {
        SnAccountAuthFactor? factor = type switch
        {
            AccountAuthFactorType.Password when !string.IsNullOrWhiteSpace(secret) => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.Password,
                Trustworthy = 1,
                AccountId = account.Id,
                Secret = secret,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            }.HashSecret(),
            AccountAuthFactorType.EmailCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.EmailCode,
                Trustworthy = 2,
                AccountId = account.Id,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            AccountAuthFactorType.InAppCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.InAppCode,
                Trustworthy = 2,
                AccountId = account.Id,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            AccountAuthFactorType.TimedCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.TimedCode,
                Trustworthy = 3,
                AccountId = account.Id,
                Secret = secret ?? Guid.NewGuid().ToString("N"),
            },
            AccountAuthFactorType.PinCode when !string.IsNullOrWhiteSpace(secret) => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.PinCode,
                Trustworthy = 1,
                AccountId = account.Id,
                Secret = secret,
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            }.HashSecret(),
            AccountAuthFactorType.RecoveryCode => new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.RecoveryCode,
                Trustworthy = 0,
                AccountId = account.Id,
                Secret = Guid.NewGuid().ToString("N"),
                EnabledAt = SystemClock.Instance.GetCurrentInstant(),
            },
            _ => null
        };

        if (factor == null) return null;
        db.AccountAuthFactors.Add(factor);
        await db.SaveChangesAsync();
        return factor;
    }

    public async Task<SnAccountAuthFactor> EnableAuthFactor(SnAccountAuthFactor factor, string? code)
    {
        if (factor.Type is AccountAuthFactorType.Password or AccountAuthFactorType.TimedCode)
        {
            factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(factor);
            await db.SaveChangesAsync();
            return factor;
        }

        if (string.IsNullOrWhiteSpace(code) || !await VerifyFactorCode(factor, code))
            throw new InvalidOperationException("Invalid factor code.");

        factor.EnabledAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(factor);
        await db.SaveChangesAsync();
        return factor;
    }

    public async Task<SnAccountAuthFactor> DisableAuthFactor(SnAccountAuthFactor factor)
    {
        factor.EnabledAt = null;
        db.Update(factor);
        await db.SaveChangesAsync();
        return factor;
    }

    public async Task DeleteAuthFactor(SnAccountAuthFactor factor)
    {
        db.AccountAuthFactors.Remove(factor);
        await db.SaveChangesAsync();
    }

    
    private async Task _SetFactorCode(SnAccountAuthFactor factor, string code, TimeSpan expires)
    {
        await cache.SetAsync(
            $"{AuthFactorCachePrefix}{factor.Id}:code",
            code,
            expires
        );
    }
    
    private async Task<string?> _GetFactorCode(SnAccountAuthFactor factor)
    {
        return await cache.GetAsync<string?>(
            $"{AuthFactorCachePrefix}{factor.Id}:code"
        );
    }

    public async Task SendFactorCode(SnAccount account, SnAccountAuthFactor factor)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        
         switch (factor.Type)
        {
            case Shared.Models.AccountAuthFactorType.InAppCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");

                await ring.SendPushNotificationToUserAsync(
                    new DySendPushNotificationToUserRequest
                    {
                        UserId = account.Id.ToString(),
                        Notification = new DyPushNotification
                        {
                            Topic = "auth.verification",
                            Title = localizer.Get("authCodeTitle", account.Language),
                            Body = localizer.Get("authCodeBody", locale: account.Language, args: new { code }),
                            IsSavable = false
                        }
                    }
                );
                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(5));
                break;
            case AccountAuthFactorType.EmailCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");

                var contact = await db.AccountContacts
                    .Where(c => c.Type == Shared.Models.AccountContactType.Email)
                    .Where(c => c.VerifiedAt != null)
                    .Where(c => c.IsPrimary)
                    .Where(c => c.AccountId == account.Id)
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync();
                if (contact is null)
                {
                    logger.LogWarning(
                        "Unable to send factor code to #{FactorId} with, due to no contact method was found...",
                        factor.Id
                    );
                    return;
                }

                await mailer
                    .SendTemplatedEmailAsync(
                        account.Nick,
                        contact.Content,
                        localizer.Get("codeEmailTitle", account.Language),
                        "FactorCode",
                        new { name = account.Name, code },
                        account.Language
                    );

                await _SetFactorCode(factor, code, TimeSpan.FromMinutes(30));
                break;
            case Shared.Models.AccountAuthFactorType.Password:
            case Shared.Models.AccountAuthFactorType.TimedCode:
            default:
                // No need to send, such as password etc...
                return;
        }
    }

    public async Task<bool> VerifyFactorCode(SnAccountAuthFactor factor, string code)
    {
        return factor.Type switch
        {
            AccountAuthFactorType.EmailCode or AccountAuthFactorType.InAppCode => await VerifyCachedFactorCode(factor, code),
            AccountAuthFactorType.Password or AccountAuthFactorType.PinCode => BCrypt.Net.BCrypt.Verify(code, factor.Secret),
            AccountAuthFactorType.TimedCode => factor.VerifyPassword(code),
            _ => false
        };
    }

    public async Task DeleteSession(SnAccount account, Guid sessionId)
    {
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.AccountId == account.Id && s.Id == sessionId);
        if (session == null) throw new InvalidOperationException("Session not found.");
        session.ExpiredAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(session);
        await db.SaveChangesAsync();
    }

    public async Task DeleteDevice(SnAccount account, string deviceId)
    {
        var client = await db.AuthClients.FirstOrDefaultAsync(c => c.AccountId == account.Id && c.DeviceId == deviceId);
        if (client == null) throw new InvalidOperationException("Device not found.");
        await db.AuthSessions
            .Where(s => s.ClientId == client.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiredAt, SystemClock.Instance.GetCurrentInstant()));
    }

    public async Task UpdateDeviceName(SnAccount account, string deviceId, string label)
    {
        var client = await db.AuthClients.FirstOrDefaultAsync(c => c.AccountId == account.Id && c.DeviceId == deviceId);
        if (client == null) throw new InvalidOperationException("Device not found.");
        client.DeviceName = label;
        db.Update(client);
        await db.SaveChangesAsync();
    }

    public async Task<SnAccountContact> CreateContactMethod(SnAccount account, AccountContactType type, string content)
    {
        var contact = new SnAccountContact
        {
            AccountId = account.Id,
            Type = type,
            Content = content,
            IsPrimary = false
        };
        db.AccountContacts.Add(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task VerifyContactMethod(SnAccount account, SnAccountContact contact)
    {
        contact.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(contact);
        await db.SaveChangesAsync();
    }

    public async Task<SnAccountContact> SetContactMethodPrimary(SnAccount account, SnAccountContact contact)
    {
        await db.AccountContacts
            .Where(c => c.AccountId == account.Id && c.Type == contact.Type)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsPrimary, false));
        contact.IsPrimary = true;
        db.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task<SnAccountContact> SetContactMethodPublic(SnAccount account, SnAccountContact contact, bool isPublic)
    {
        contact.IsPublic = isPublic;
        db.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task DeleteContactMethod(SnAccount account, SnAccountContact contact)
    {
        db.AccountContacts.Remove(contact);
        await db.SaveChangesAsync();
    }

    public async Task<bool> ActivateAccountAndGrantDefaultPermissions(Guid accountId, Instant activatedAt)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return false;

        if (account.ActivatedAt is null || account.ActivatedAt < activatedAt)
        {
            account.ActivatedAt = activatedAt;
            db.Accounts.Update(account);
        }

        var actor = accountId.ToString();
        var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
        if (defaultGroup is not null)
        {
            var memberExists = await db.PermissionGroupMembers
                .AnyAsync(m => m.GroupId == defaultGroup.Id && m.Actor == actor);
            if (!memberExists)
            {
                db.PermissionGroupMembers.Add(new SnPermissionGroupMember
                {
                    GroupId = defaultGroup.Id,
                    Actor = actor
                });
            }
        }

        await db.SaveChangesAsync();
        return true;
    }

    private async Task PublishAccountCreated(SnAccount account)
    {
        var primaryEmail = account.Contacts
            .Where(c => c.Type == AccountContactType.Email)
            .OrderByDescending(c => c.IsPrimary)
            .FirstOrDefault();

        await eventBus.PublishAsync(AccountCreatedEvent.Type, new AccountCreatedEvent
        {
            AccountId = account.Id,
            Name = account.Name,
            Nick = account.Nick,
            Language = account.Language,
            Region = account.Region,
            PrimaryEmail = primaryEmail?.Content,
            PrimaryEmailVerifiedAt = primaryEmail?.VerifiedAt,
            ActivatedAt = account.ActivatedAt,
            IsSuperuser = account.IsSuperuser,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
    }

    private async Task<bool> VerifyCachedFactorCode(SnAccountAuthFactor factor, string code)
    {
        var cached = await cache.GetAsync<string>($"{AuthFactorCachePrefix}{factor.Id}:code");
        if (!string.Equals(cached, code, StringComparison.Ordinal))
            return false;
        await cache.RemoveAsync($"{AuthFactorCachePrefix}{factor.Id}:code");
        return true;
    }

    private async Task<string> GenerateAvailableUsername(string baseName)
    {
        var normalized = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "user";

        var candidate = normalized;
        var suffix = 1;
        while (await CheckAccountNameHasTaken(candidate))
        {
            candidate = $"{normalized}{suffix++}";
        }

        return candidate;
    }
     public async Task<SnAccount> CreateBotAccount(
        SnAccount account,
        Guid automatedId,
        string? pictureId,
        string? backgroundId
    )
    {
        var dupeAutomateCount = await db.Set<SnAccount>().Where(a => a.AutomatedId == automatedId).CountAsync();
        if (dupeAutomateCount > 0)
            throw new InvalidOperationException("Automated ID has already been used.");

        var dupeNameCount = await db.Set<SnAccount>().Where(a => a.Name == account.Name).CountAsync();
        if (dupeNameCount > 0)
            throw new InvalidOperationException("Account name has already been taken.");

        account.AutomatedId = automatedId;
        account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
        account.IsSuperuser = false;

        if (!string.IsNullOrEmpty(pictureId))
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = pictureId });
            account.Profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (!string.IsNullOrEmpty(backgroundId))
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = backgroundId });
            account.Profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
        if (defaultGroup is not null)
        {
            db.PermissionGroupMembers.Add(new SnPermissionGroupMember
            {
                Actor = account.Id.ToString(),
                Group = defaultGroup
            });
        }

        db.Set<SnAccount>().Add(account);
        await db.SaveChangesAsync();

        return account;
    }

    public async Task<SnAccount?> GetBotAccount(Guid automatedId)
    {
        return await db.Accounts.FirstOrDefaultAsync(a => a.AutomatedId == automatedId);
    }

    public async Task DeleteAccount(SnAccount account)
    {
        logger.LogWarning("Deleting account {AccountId}", account.Id);
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.Set<SnAuthSession>()
            .Where(s => s.AccountId == account.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, now));

        db.Set<SnAccount>().Remove(account);
        await db.SaveChangesAsync();

        await eventBus.PublishAsync(AccountDeletedEvent.Type, new AccountDeletedEvent
        {
            AccountId = account.Id,
            DeletedAt = SystemClock.Instance.GetCurrentInstant()
        });
    }

    public async Task<SnAccount> UpdateBasicInfo(SnAccount account, string? nick, string? language, string? region)
    {
        var dbAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id);
        if (dbAccount is null)
            throw new InvalidOperationException("Account not found.");

        if (nick is not null) dbAccount.Nick = nick;
        if (language is not null) dbAccount.Language = language;
        if (region is not null) dbAccount.Region = region;

        db.Update(dbAccount);
        await db.SaveChangesAsync();

        return dbAccount;
    }
}
