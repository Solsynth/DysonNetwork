using System.Globalization;
using System.Security.Cryptography;
using DysonNetwork.Sphere.Account.Email;
using DysonNetwork.Sphere.Pages.Emails;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Resources.Localization;
using DysonNetwork.Sphere.Resources.Pages.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class MagicSpellService(
    AppDatabase db,
    EmailService email,
    IConfiguration configuration,
    ILogger<MagicSpellService> logger,
    IStringLocalizer<Localization.EmailResource> localizer
)
{
    public async Task<MagicSpell> CreateMagicSpell(
        Account account,
        MagicSpellType type,
        Dictionary<string, object> meta,
        Instant? expiredAt = null,
        Instant? affectedAt = null
    )
    {
        var spellWord = _GenerateRandomString(128);
        var spell = new MagicSpell
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

    public async Task NotifyMagicSpell(MagicSpell spell, bool bypassVerify = false)
    {
        var contact = await db.AccountContacts
            .Where(c => c.Account.Id == spell.AccountId)
            .Where(c => c.Type == AccountContactType.Email)
            .Where(c => c.VerifiedAt != null || bypassVerify)
            .Include(c => c.Account)
            .FirstOrDefaultAsync();
        if (contact is null) throw new ArgumentException("Account has no contact method that can use");

        var link = $"{configuration.GetValue<string>("BaseUrl")}/spells/{Uri.EscapeDataString(spell.Spell)}";

        logger.LogInformation("Sending magic spell... {Link}", link);

        var accountLanguage = await db.Accounts
            .Where(a => a.Id == spell.AccountId)
            .Select(a => a.Language)
            .FirstOrDefaultAsync();

        var cultureInfo = new CultureInfo(accountLanguage ?? "en-us", false);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

        try
        {
            switch (spell.Type)
            {
                case MagicSpellType.AccountActivation:
                    await email.SendTemplatedEmailAsync<LandingEmail, LandingEmailModel>(
                        contact.Account.Name,
                        contact.Content,
                        localizer["EmailLandingTitle"],
                        new LandingEmailModel
                        {
                            Name = contact.Account.Name,
                            VerificationLink = link
                        }
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception err)
        {
            logger.LogError($"Error sending magic spell (${spell.Spell})... {err}");
        }
    }

    public async Task ApplyMagicSpell(MagicSpell spell)
    {
        switch (spell.Type)
        {
            case MagicSpellType.AccountActivation:
                var contactMethod = spell.Meta["contact_method"] as string;
                var contact = await
                    db.AccountContacts.FirstOrDefaultAsync(c =>
                        c.Account.Id == spell.AccountId && c.Content == contactMethod
                    );
                if (contact is not null)
                {
                    contact.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
                    db.Update(contact);
                }

                var account = await db.Accounts.FirstOrDefaultAsync(c => c.Id == spell.AccountId);
                if (account is not null)
                {
                    account.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
                    db.Update(account);
                }

                var defaultGroup = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Key == "default");
                if (defaultGroup is not null && account is not null)
                {
                    db.PermissionGroupMembers.Add(new PermissionGroupMember
                    {
                        Actor = $"user:{account.Id}",
                        Group = defaultGroup
                    });
                }

                db.Remove(spell);
                await db.SaveChangesAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static string _GenerateRandomString(int length)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[length];
        rng.GetBytes(randomBytes);

        var base64String = Convert.ToBase64String(randomBytes);

        return base64String.Substring(0, length);
    }
}