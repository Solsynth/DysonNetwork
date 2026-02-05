using System.Globalization;
using DysonNetwork.Pass.Affiliation;
using DysonNetwork.Pass.Auth.OpenId;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Pass.Mailer;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Net;
using NodaTime;
using OtpNet;
using AuthService = DysonNetwork.Pass.Auth.AuthService;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Pass.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    FileService.FileServiceClient files,
    AccountUsernameService uname,
    AffiliationSpellService ars,
    EmailService mailer,
    RingService.RingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    ILogger<AccountService> logger,
    RemoteSubscriptionService remoteSubscription,
    INatsConnection nats
)
{
    public const string AccountCachePrefix = "account:";

    public async Task PurgeAccountCache(SnAccount account)
    {
        await cache.RemoveGroupAsync($"{AccountCachePrefix}{account.Id}");
    }

    public async Task<SnAccount?> GetAccount(Guid id)
    {
        return await db.Accounts
            .Where(a => a.Id == id)
            .Include(a => a.Profile)
            .FirstOrDefaultAsync();
    }

    public async Task<SnAccount?> LookupAccount(string probe)
    {
        var account = await db.Accounts.Where(a => EF.Functions.ILike(a.Name, probe)).FirstOrDefaultAsync();
        if (account is not null) return account;

        var contact = await db.AccountContacts
            .Where(c => c.Type == Shared.Models.AccountContactType.Email ||
                        c.Type == Shared.Models.AccountContactType.PhoneNumber)
            .Where(c => EF.Functions.ILike(c.Content, probe))
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        return contact?.Account;
    }

    public async Task<SnAccount?> LookupAccountByConnection(string identifier, string provider)
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

    public async Task<bool> CheckAccountNameHasTaken(string name)
    {
        return await db.Accounts.AnyAsync(a => EF.Functions.ILike(a.Name, name));
    }

    public async Task<bool> CheckEmailHasBeenUsed(string email)
    {
        return await db.AccountContacts.AnyAsync(c =>
            c.Type == Shared.Models.AccountContactType.Email && EF.Functions.ILike(c.Content, email));
    }

    public async Task<SnAccount> CreateAccount(
        string name,
        string nick,
        string email,
        string? password,
        string language = "en-US",
        string region = "en",
        string? affiliationSpell = null,
        bool isEmailVerified = false,
        bool isActivated = false
    )
    {
        if (await CheckAccountNameHasTaken(name))
            throw new InvalidOperationException("Account name has already been taken.");

        var dupeEmailCount = await db.AccountContacts
            .Where(c => c.Content == email && c.Type == Shared.Models.AccountContactType.Email
            ).CountAsync();
        if (dupeEmailCount > 0)
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
                    Type = Shared.Models.AccountContactType.Email,
                    Content = email,
                    VerifiedAt = isEmailVerified ? SystemClock.Instance.GetCurrentInstant() : null,
                    IsPrimary = true
                }
            ],
            AuthFactors = password is not null
                ? new List<SnAccountAuthFactor>
                {
                    new SnAccountAuthFactor
                    {
                        Type = Shared.Models.AccountAuthFactorType.Password,
                        Secret = password,
                        EnabledAt = SystemClock.Instance.GetCurrentInstant()
                    }.HashSecret()
                }
                : [],
            Profile = new SnAccountProfile()
        };

        if (affiliationSpell is not null)
            await ars.CreateAffiliationResult(affiliationSpell, $"account:{account.Id}");

        if (isActivated)
        {
            account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
            var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
            if (defaultGroup is not null)
            {
                db.PermissionGroupMembers.Add(new SnPermissionGroupMember
                {
                    Actor = account.Id.ToString(),
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

    public async Task<SnAccount> CreateAccount(OidcUserInfo userInfo)
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
            isEmailVerified: userInfo.EmailVerified
        );
    }

    public async Task<SnAccount> CreateBotAccount(SnAccount account, Guid automatedId, string? pictureId,
        string? backgroundId)
    {
        var dupeAutomateCount = await db.Accounts.Where(a => a.AutomatedId == automatedId).CountAsync();
        if (dupeAutomateCount > 0)
            throw new InvalidOperationException("Automated ID has already been used.");

        var dupeNameCount = await db.Accounts.Where(a => a.Name == account.Name).CountAsync();
        if (dupeNameCount > 0)
            throw new InvalidOperationException("Account name has already been taken.");

        account.AutomatedId = automatedId;
        account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
        account.IsSuperuser = false;

        if (!string.IsNullOrEmpty(pictureId))
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = pictureId });
            account.Profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);

            await files.SetFilePublicAsync(new SetFilePublicRequest { FileId = pictureId });
        }

        if (!string.IsNullOrEmpty(backgroundId))
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = backgroundId });
            account.Profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);

            await files.SetFilePublicAsync(new SetFilePublicRequest { FileId = backgroundId });
        }

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return account;
    }

    public async Task<SnAccount?> GetBotAccount(Guid automatedId)
    {
        return await db.Accounts.FirstOrDefaultAsync(a => a.AutomatedId == automatedId);
    }

    public async Task RequestAccountDeletion(SnAccount account)
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

    public async Task RequestPasswordReset(SnAccount account)
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

    public async Task<bool> CheckAuthFactorExists(SnAccount account, Shared.Models.AccountAuthFactorType type)
    {
        var isExists = await db.AccountAuthFactors
            .Where(x => x.AccountId == account.Id && x.Type == type)
            .AnyAsync();
        return isExists;
    }

    public async Task<SnAccountAuthFactor?> CreateAuthFactor(SnAccount account,
        Shared.Models.AccountAuthFactorType type, string? secret)
    {
        SnAccountAuthFactor? factor = null;
        switch (type)
        {
            case Shared.Models.AccountAuthFactorType.Password:
                if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentNullException(nameof(secret));
                factor = new SnAccountAuthFactor
                {
                    Type = Shared.Models.AccountAuthFactorType.Password,
                    Trustworthy = 1,
                    AccountId = account.Id,
                    Secret = secret,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                }.HashSecret();
                break;
            case Shared.Models.AccountAuthFactorType.EmailCode:
                factor = new SnAccountAuthFactor
                {
                    Type = Shared.Models.AccountAuthFactorType.EmailCode,
                    Trustworthy = 2,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant(),
                };
                break;
            case Shared.Models.AccountAuthFactorType.InAppCode:
                factor = new SnAccountAuthFactor
                {
                    Type = Shared.Models.AccountAuthFactorType.InAppCode,
                    Trustworthy = 1,
                    EnabledAt = SystemClock.Instance.GetCurrentInstant()
                };
                break;
            case Shared.Models.AccountAuthFactorType.TimedCode:
                var skOtp = KeyGeneration.GenerateRandomKey(20);
                var skOtp32 = Base32Encoding.ToString(skOtp);
                factor = new SnAccountAuthFactor
                {
                    Secret = skOtp32,
                    Type = Shared.Models.AccountAuthFactorType.TimedCode,
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
            case Shared.Models.AccountAuthFactorType.PinCode:
                if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentNullException(nameof(secret));
                if (!secret.All(char.IsDigit) || secret.Length != 6)
                    throw new ArgumentException("PIN code must be exactly 6 digits");
                factor = new SnAccountAuthFactor
                {
                    Type = Shared.Models.AccountAuthFactorType.PinCode,
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

    public async Task<SnAccountAuthFactor> EnableAuthFactor(SnAccountAuthFactor factor, string? code)
    {
        if (factor.EnabledAt is not null) throw new ArgumentException("The factor has been enabled.");
        if (factor.Type is Shared.Models.AccountAuthFactorType.Password
            or Shared.Models.AccountAuthFactorType.TimedCode)
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

    public async Task<SnAccountAuthFactor> DisableAuthFactor(SnAccountAuthFactor factor)
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

    public async Task DeleteAuthFactor(SnAccountAuthFactor factor)
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
    /// </summary>
    /// <param name="account">The owner of the auth factor</param>
    /// <param name="factor">The auth factor needed to send code</param>
    public async Task SendFactorCode(SnAccount account, SnAccountAuthFactor factor)
    {
        var code = new Random().Next(100000, 999999).ToString("000000");

        switch (factor.Type)
        {
            case Shared.Models.AccountAuthFactorType.InAppCode:
                if (await _GetFactorCode(factor) is not null)
                    throw new InvalidOperationException("A factor code has been sent and in active duration.");

                await pusher.SendPushNotificationToUserAsync(
                    new SendPushNotificationToUserRequest
                    {
                        UserId = account.Id.ToString(),
                        Notification = new PushNotification
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
            case Shared.Models.AccountAuthFactorType.EmailCode:
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
                    .SendRazorTemplateEmailAsync<VerificationEmailModel>(
                        account.Nick,
                        contact.Content,
                        localizer.Get("codeEmailTitle", account.Language),
                        "FactorCode",
                        new VerificationEmailModel
                        {
                            Name = account.Name,
                            Code = code
                        },
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
        switch (factor.Type)
        {
            case Shared.Models.AccountAuthFactorType.EmailCode:
            case Shared.Models.AccountAuthFactorType.InAppCode:
                var correctCode = await _GetFactorCode(factor);
                var isCorrect = correctCode is not null &&
                                string.Equals(correctCode, code, StringComparison.OrdinalIgnoreCase);
                await cache.RemoveAsync($"{AuthFactorCachePrefix}{factor.Id}:code");
                return isCorrect;
            case Shared.Models.AccountAuthFactorType.Password:
            case Shared.Models.AccountAuthFactorType.TimedCode:
            default:
                return factor.VerifyPassword(code);
        }
    }

    private const string AuthFactorCachePrefix = "authfactor:";

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

    private async Task<bool> IsDeviceActive(Guid id)
    {
        return await db.AuthSessions.AnyAsync(s => s.ClientId == id);
    }

    public async Task<SnAuthClient> UpdateDeviceName(SnAccount account, string deviceId, string label)
    {
        var device = await db.AuthClients.FirstOrDefaultAsync(c => c.DeviceId == deviceId && c.AccountId == account.Id
        );
        if (device is null) throw new InvalidOperationException("Device was not found.");

        device.DeviceLabel = label;
        db.Update(device);
        await db.SaveChangesAsync();

        return device;
    }

    public async Task DeleteSession(SnAccount account, Guid sessionId)
    {
        var session = await db.AuthSessions
            .Include(s => s.Client)
            .Where(s => s.Id == sessionId && s.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (session is null) throw new InvalidOperationException("Session was not found.");

        // The current session should be included in the sessions' list
        db.AuthSessions.Remove(session);
        await db.SaveChangesAsync();

        if (session.ClientId.HasValue)
        {
            if (!await IsDeviceActive(session.ClientId.Value))
                await pusher.UnsubscribePushNotificationsAsync(new UnsubscribePushNotificationsRequest()
                { DeviceId = session.Client!.DeviceId }
                );
        }

        logger.LogInformation("Deleted session #{SessionId}", session.Id);

        await cache.RemoveAsync($"{AuthService.AuthCachePrefix}{session.Id}");
    }

    public async Task DeleteDevice(SnAccount account, string deviceId)
    {
        var device = await db.AuthClients.FirstOrDefaultAsync(c => c.DeviceId == deviceId && c.AccountId == account.Id
        );
        if (device is null)
            throw new InvalidOperationException("Device not found.");

        await pusher.UnsubscribePushNotificationsAsync(
            new UnsubscribePushNotificationsRequest { DeviceId = device.DeviceId }
        );

        var sessions = await db.AuthSessions
            .Where(s => s.ClientId == device.Id && s.AccountId == account.Id)
            .ToListAsync();

        // The current session should be included in the sessions' list
        var now = SystemClock.Instance.GetCurrentInstant();
        await db.AuthSessions
            .Where(s => s.ClientId == device.Id)
            .ExecuteUpdateAsync(p => p.SetProperty(s => s.DeletedAt, s => now));

        db.AuthClients.Remove(device);
        await db.SaveChangesAsync();

        foreach (var item in sessions)
            await cache.RemoveAsync($"{AuthService.AuthCachePrefix}{item.Id}");
    }

    public async Task<SnAccountContact> CreateContactMethod(SnAccount account, Shared.Models.AccountContactType type,
        string content)
    {
        var isExists = await db.AccountContacts
            .Where(x => x.AccountId == account.Id && x.Type == type && x.Content == content)
            .AnyAsync();
        if (isExists)
            throw new InvalidOperationException("Contact method already exists.");

        var contact = new SnAccountContact
        {
            Type = type,
            Content = content,
            AccountId = account.Id,
        };

        db.AccountContacts.Add(contact);
        await db.SaveChangesAsync();

        return contact;
    }

    public async Task VerifyContactMethod(SnAccount account, SnAccountContact contact)
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

    public async Task<SnAccountContact> SetContactMethodPrimary(SnAccount account, SnAccountContact contact)
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

    public async Task<SnAccountContact> SetContactMethodPublic(SnAccount account, SnAccountContact contact,
        bool isPublic)
    {
        contact.IsPublic = isPublic;
        db.AccountContacts.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task DeleteContactMethod(SnAccount account, SnAccountContact contact)
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
    public async Task<SnAccountBadge> GrantBadge(SnAccount account, SnAccountBadge badge)
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
    public async Task RevokeBadge(SnAccount account, Guid badgeId)
    {
        var badge = await db.Badges
            .Where(b => b.AccountId == account.Id && b.Id == badgeId)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync() ?? throw new InvalidOperationException("Badge was not found.");
        var profile = await db.AccountProfiles
            .Where(p => p.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (profile?.ActiveBadge is not null && profile.ActiveBadge.Id == badge.Id)
            profile.ActiveBadge = null;

        db.Remove(badge);
        await db.SaveChangesAsync();
    }

    public async Task ActiveBadge(SnAccount account, Guid badgeId)
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

    public async Task DeleteAccount(SnAccount account)
    {
        await db.AuthSessions
            .Where(s => s.AccountId == account.Id)
            .ExecuteDeleteAsync();

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();

        var js = nats.CreateJetStreamContext();
        await js.PublishAsync(
            AccountDeletedEvent.Type,
            GrpcTypeHelper.ConvertObjectToByteString(new AccountDeletedEvent
            {
                AccountId = account.Id,
                DeletedAt = SystemClock.Instance.GetCurrentInstant()
            }).ToByteArray()
        );
    }

    /// <summary>
    /// Populates the PerkSubscription property for a single account by calling the Wallet service via gRPC.
    /// </summary>
    public async Task PopulatePerkSubscriptionAsync(SnAccount account)
    {
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate PerkSubscription for account {AccountId}", account.Id);
        }
    }

    /// <summary>
    /// Populates the PerkSubscription property for multiple accounts by calling the Wallet service via gRPC.
    /// </summary>
    public async Task PopulatePerkSubscriptionsAsync(List<SnAccount> accounts)
    {
        if (accounts.Count == 0) return;

        try
        {
            var accountIds = accounts.Select(a => a.Id).ToList();
            var subscriptions = await remoteSubscription.GetPerkSubscriptions(accountIds);

            var subscriptionDict = subscriptions
                .Where(s => s != null)
                .ToDictionary(
                    s => Guid.Parse(s.AccountId),
                    s => SnWalletSubscription.FromProtoValue(s).ToReference()
                );

            foreach (var account in accounts)
            {
                if (subscriptionDict.TryGetValue(account.Id, out var subscription))
                {
                    account.PerkSubscription = subscription;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to populate PerkSubscriptions for {Count} accounts", accounts.Count);
        }
    }
}
