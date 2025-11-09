using System.Security.Cryptography;
using System.Text.Json;
using DysonNetwork.Pass.Mailer;
using DysonNetwork.Pass.Resources.Emails;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;
using EmailResource = DysonNetwork.Pass.Localization.EmailResource;

namespace DysonNetwork.Pass.Account;

public class MagicSpellService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<MagicSpellService> logger,
    IStringLocalizer<EmailResource> localizer,
    EmailService email,
    ICacheService cache
)
{
    public async Task<SnMagicSpell> CreateMagicSpell(
        SnAccount account,
        MagicSpellType type,
        Dictionary<string, object> meta,
        Instant? expiredAt = null,
        Instant? affectedAt = null,
        bool preventRepeat = false
    )
    {
        if (preventRepeat)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var existingSpell = await db.MagicSpells
                .Where(s => s.AccountId == account.Id)
                .Where(s => s.Type == type)
                .Where(s => s.ExpiresAt == null || s.ExpiresAt > now)
                .FirstOrDefaultAsync();
            if (existingSpell is not null)
                return existingSpell;
        }

        var spellWord = _GenerateRandomString(128);
        var spell = new SnMagicSpell
        {
            Spell = spellWord,
            Type = type,
            ExpiresAt = expiredAt,
            AffectedAt = affectedAt,
            AccountId = account.Id,
            Meta = meta
        };

        db.MagicSpells.Add(spell);
        await db.SaveChangesAsync();

        return spell;
    }

    private const string SpellNotifyCacheKeyPrefix = "spells:notify:";

    public async Task NotifyMagicSpell(SnMagicSpell spell, bool bypassVerify = false)
    {
        var cacheKey = SpellNotifyCacheKeyPrefix + spell.Id;
        var (found, _) = await cache.GetAsyncWithStatus<bool?>(cacheKey);
        if (found)
        {
            logger.LogInformation("Skip sending magic spell {SpellId} due to already sent.", spell.Id);
            return;
        }

        var contact = await db.AccountContacts
            .Where(c => c.Account.Id == spell.AccountId)
            .Where(c => c.Type == AccountContactType.Email)
            .Where(c => c.VerifiedAt != null || bypassVerify)
            .OrderByDescending(c => c.IsPrimary)
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        if (contact is null) throw new ArgumentException("Account has no contact method that can use");

        var link = $"{configuration.GetValue<string>("SiteUrl")}/spells/{Uri.EscapeDataString(spell.Spell)}";

        logger.LogInformation("Sending magic spell... {Link}", link);

        var accountLanguage = await db.Accounts
            .Where(a => a.Id == spell.AccountId)
            .Select(a => a.Language)
            .FirstOrDefaultAsync();
        AccountService.SetCultureInfo(accountLanguage);

        try
        {
            switch (spell.Type)
            {
                case MagicSpellType.AccountActivation:
                    await email.SendTemplatedEmailAsync<RegistrationConfirmEmail, LandingEmailModel>(
                        contact.Account.Nick,
                        contact.Content,
                        localizer["RegConfirmTitle"],
                        new LandingEmailModel
                        {
                            Name = contact.Account.Name,
                            Link = link
                        }
                    );
                    break;
                case MagicSpellType.AccountRemoval:
                    await email.SendTemplatedEmailAsync<AccountDeletionEmail, AccountDeletionEmailModel>(
                        contact.Account.Nick,
                        contact.Content,
                        localizer["AccountDeletionTitle"],
                        new AccountDeletionEmailModel
                        {
                            Name = contact.Account.Name,
                            Link = link
                        }
                    );
                    break;
                case MagicSpellType.AuthPasswordReset:
                    await email.SendTemplatedEmailAsync<PasswordResetEmail, PasswordResetEmailModel>(
                        contact.Account.Nick,
                        contact.Content,
                        localizer["PasswordResetTitle"],
                        new PasswordResetEmailModel
                        {
                            Name = contact.Account.Name,
                            Link = link
                        }
                    );
                    break;
                case MagicSpellType.ContactVerification:
                    if (spell.Meta["contact_method"] is not string contactMethod)
                        throw new InvalidOperationException("Contact method is not found.");
                    await email.SendTemplatedEmailAsync<ContactVerificationEmail, ContactVerificationEmailModel>(
                        contact.Account.Nick,
                        contactMethod!,
                        localizer["ContractVerificationTitle"],
                        new ContactVerificationEmailModel
                        {
                            Name = contact.Account.Name,
                            Link = link
                        }
                    );
                    break;
                case MagicSpellType.AccountDeactivation:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await cache.SetAsync(cacheKey, true, TimeSpan.FromMinutes(5));
        }
        catch (Exception err)
        {
            logger.LogError($"Error sending magic spell (${spell.Spell})... {err}");
        }
    }

    public async Task ApplyMagicSpell(SnMagicSpell spell)
    {
        switch (spell.Type)
        {
            case MagicSpellType.AuthPasswordReset:
                throw new ArgumentException(
                    "For password reset spell, please use the ApplyPasswordReset method instead."
                );
            case MagicSpellType.AccountRemoval:
                var account = await db.Accounts.FirstOrDefaultAsync(c => c.Id == spell.AccountId);
                if (account is null) break;
                db.Accounts.Remove(account);
                break;
            case MagicSpellType.AccountActivation:
                var contactMethod = (spell.Meta["contact_method"] as JsonElement? ?? default).ToString();
                var contact = await
                    db.AccountContacts.FirstOrDefaultAsync(c =>
                        c.Content == contactMethod
                    );
                if (contact is not null)
                {
                    contact.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
                    db.Update(contact);
                }

                account = await db.Accounts.FirstOrDefaultAsync(c => c.Id == spell.AccountId);
                if (account is not null)
                {
                    account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
                    db.Update(account);
                }

                var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
                if (defaultGroup is not null && account is not null)
                {
                    db.PermissionGroupMembers.Add(new SnPermissionGroupMember
                    {
                        Actor = $"user:{account.Id}",
                        Group = defaultGroup
                    });
                }

                break;
            case MagicSpellType.ContactVerification:
                var verifyContactMethod = (spell.Meta["contact_method"] as JsonElement? ?? default).ToString();
                var verifyContact = await db.AccountContacts
                    .FirstOrDefaultAsync(c => c.Content == verifyContactMethod);
                if (verifyContact is not null)
                {
                    verifyContact.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
                    db.Update(verifyContact);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        db.Remove(spell);
        await db.SaveChangesAsync();
    }

    public async Task ApplyPasswordReset(SnMagicSpell spell, string newPassword)
    {
        if (spell.Type != MagicSpellType.AuthPasswordReset)
            throw new ArgumentException("This spell is not a password reset spell.");

        var passwordFactor = await db.AccountAuthFactors
            .Include(f => f.Account)
            .Where(f => f.Type == AccountAuthFactorType.Password && f.Account.Id == spell.AccountId)
            .FirstOrDefaultAsync();
        if (passwordFactor is null)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(c => c.Id == spell.AccountId);
            if (account is null) throw new InvalidOperationException("Both account and auth factor was not found.");
            passwordFactor = new SnAccountAuthFactor
            {
                Type = AccountAuthFactorType.Password,
                Account = account,
                Secret = newPassword
            }.HashSecret();
            db.AccountAuthFactors.Add(passwordFactor);
        }
        else
        {
            passwordFactor.Secret = newPassword;
            passwordFactor.HashSecret();
            db.Update(passwordFactor);
        }

        await db.SaveChangesAsync();
    }

    private static string _GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var result = new char[length];
        using var rng = RandomNumberGenerator.Create();
        for (var i = 0; i < length; i++)
        {
            var bytes = new byte[1];
            rng.GetBytes(bytes);
            result[i] = chars[bytes[0] % chars.Length];
        }
        return new string(result);
    }
}
