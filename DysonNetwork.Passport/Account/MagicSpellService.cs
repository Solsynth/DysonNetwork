using System.Security.Cryptography;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Proto;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class MagicSpellService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<MagicSpellService> logger,
    ILocalizationService localizer,
    EmailService email,
    ICacheService cache,
    IEventBus eventBus,
    RemoteAccountContactService remoteContacts,
    DyAccountService.DyAccountServiceClient remoteAccounts
)
{
    public async Task<SnMagicSpell> CreateMagicSpell(
        SnAccount account,
        MagicSpellType type,
        Dictionary<string, object> meta,
        Instant? expiredAt = null,
        Instant? affectedAt = null,
        string? code = null,
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

        var spellWord = code ?? _GenerateRandomString(128);
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

        if (!spell.AccountId.HasValue)
            throw new ArgumentException("Spell is missing account id.");

        var contacts = await remoteContacts.ListContactsAsync(
            spell.AccountId.Value,
            AccountContactType.Email,
            verifiedOnly: !bypassVerify
        );
        var contact = contacts
            .OrderByDescending(c => c.IsPrimary)
            .FirstOrDefault();
        if (contact is null) throw new ArgumentException("Account has no contact method that can use");

        var account = await remoteAccounts.GetAccountAsync(new DyGetAccountRequest
        {
            Id = spell.AccountId.Value.ToString()
        });

        var link = $"{configuration.GetValue<string>("SiteUrl")}/spells/{Uri.EscapeDataString(spell.Spell)}";

        logger.LogInformation("Sending magic spell... {Link}", link);

        var accountLanguage = account.Language;

        try
        {
            switch (spell.Type)
            {
                case MagicSpellType.AccountActivation:
                    await email.SendTemplatedEmailAsync(
                        contact.Account.Nick,
                        contact.Content,
                        localizer.Get("regConfirmTitle", accountLanguage),
                        "Welcome",
                        new { name = account.Name, link },
                        accountLanguage
                    );
                    break;
                case MagicSpellType.AccountRemoval:
                    await email.SendTemplatedEmailAsync(
                        contact.Account.Nick,
                        contact.Content,
                        localizer.Get("accountDeletionTitle", accountLanguage),
                        "AccountDeletion",
                        new { name = account.Name, link },
                        accountLanguage
                    );
                    break;
                case MagicSpellType.AuthPasswordReset:
                    await email.SendTemplatedEmailAsync(
                        contact.Account.Nick,
                        contact.Content,
                        localizer.Get("passwordResetTitle", accountLanguage),
                        "PasswordReset",
                        new { name = account.Name, link },
                        accountLanguage
                    );
                    break;
                case MagicSpellType.ContactVerification:
                    if (spell.Meta["contact_method"] is not string contactMethod)
                        throw new InvalidOperationException("Contact method is not found.");
                    await email.SendTemplatedEmailAsync(
                        contact.Account.Nick,
                        contactMethod!,
                        localizer.Get("contractMethodVerificationTitle", accountLanguage),
                        "ContactVerification",
                        new { name = account.Name, link },
                        accountLanguage
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
        AccountActivatedEvent? accountActivatedEvent = null;

        switch (spell.Type)
        {
            case MagicSpellType.AuthPasswordReset:
                throw new ArgumentException(
                    "For password reset spell, please use the ApplyPasswordReset method instead."
                );
            case MagicSpellType.AccountRemoval:
                // Account/auth deletion is now owned by Padlock. Passport only clears profile-domain projections.
                if (spell.AccountId.HasValue)
                {
                    var accountId = spell.AccountId.Value;
                    await db.AccountProfiles.Where(p => p.AccountId == accountId).ExecuteDeleteAsync();
                    await db.AccountStatuses.Where(s => s.AccountId == accountId).ExecuteDeleteAsync();
                    await db.PresenceActivities.Where(p => p.AccountId == accountId).ExecuteDeleteAsync();
                    await db.AccountRelationships
                        .Where(r => r.AccountId == accountId || r.RelatedId == accountId)
                        .ExecuteDeleteAsync();
                    await db.Badges.Where(b => b.AccountId == accountId).ExecuteDeleteAsync();
                    await db.RealmMembers.Where(m => m.AccountId == accountId).ExecuteDeleteAsync();
                    await db.PermissionGroupMembers
                        .Where(m => m.Actor == accountId.ToString())
                        .ExecuteDeleteAsync();
                }
                break;
            case MagicSpellType.AccountActivation:
                if (spell.AccountId.HasValue)
                {
                    var activatedAt = SystemClock.Instance.GetCurrentInstant();
                    accountActivatedEvent = new AccountActivatedEvent
                    {
                        AccountId = spell.AccountId.Value,
                        ActivatedAt = activatedAt
                    };
                }
                break;
            case MagicSpellType.ContactVerification:
                // Contact data is now owned by Padlock; verification state should be applied there.
                logger.LogInformation("Contact verification spell {SpellId} applied in Passport (no local contact write).", spell.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        db.Remove(spell);
        await db.SaveChangesAsync();

        if (accountActivatedEvent is not null)
            await eventBus.PublishAsync(AccountActivatedEvent.Type, accountActivatedEvent);
    }

    public async Task ApplyPasswordReset(SnMagicSpell spell, string newPassword)
    {
        if (spell.Type != MagicSpellType.AuthPasswordReset)
            throw new ArgumentException("This spell is not a password reset spell.");
        throw new InvalidOperationException("Password reset has moved to Padlock. Please use Padlock auth endpoints.");
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
