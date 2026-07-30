using System.Security.Cryptography;
using System.Globalization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace DysonNetwork.Passport.Affiliation;

public class AffiliationSpellService(AppDatabase db, RemotePaymentService payments, IOptions<AffiliationPurchaseOptions> options, IClock clock)
{
    private readonly AffiliationPurchaseOptions _options = options.Value;
    public async Task<SnAffiliationResult> RecordAffiliationEvent(string spellWord, string resourceIdentifier)
    {
        var spell = await db.AffiliationSpells.FirstOrDefaultAsync(a => a.Spell == spellWord);
        if (spell is null) throw new InvalidOperationException("The spell was not found.");

        var result = new SnAffiliationResult
        {
            Spell = spell,
            ResourceIdentifier = resourceIdentifier
        };

        db.AffiliationResults.Add(result);
        await db.SaveChangesAsync();

        return result;
    }

    public async Task<SnAffiliationSpell> CreateAffiliationSpell(Guid accountId, string? spellWord)
    {
        spellWord ??= _GenerateRandomString(8);
        if (await CheckAffiliationSpellHasTaken(spellWord))
            throw new InvalidOperationException("The spell has been taken.");

        var spell = new SnAffiliationSpell
        {
            AccountId = accountId,
            Spell = spellWord
        };

        db.AffiliationSpells.Add(spell);
        await db.SaveChangesAsync();
        return spell;
    }

    public async Task<SnAffiliationSpellPurchase> PurchaseRegistrationInvite(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Affiliation invite purchases are disabled.");
        var since = clock.GetCurrentInstant() - Duration.FromDays(_options.PurchasePeriodDays);
        var count = await db.AffiliationSpellPurchases.CountAsync(x => x.AccountId == accountId && x.CreatedAt >= since, cancellationToken);
        if (count >= _options.MaxPurchases) throw new InvalidOperationException("The purchase limit has been reached for this period.");

        var purchase = new SnAffiliationSpellPurchase { AccountId = accountId, Amount = _options.PricePoints };
        var order = await payments.CreateOrder(
            currency: "points",
            amount: _options.PricePoints.ToString(CultureInfo.InvariantCulture),
            productIdentifier: "affiliations.registration-invite",
            remarks: "Purchase registration invitation spell",
            meta: DysonNetwork.Shared.Data.InfraObjectCoder.ConvertObjectToByteString(new Dictionary<string, object?>
            {
                ["account_id"] = accountId,
                ["purchase_id"] = purchase.Id
            }).ToByteArray());
        purchase.OrderId = Guid.Parse(order.Id);
        db.AffiliationSpellPurchases.Add(purchase);
        await db.SaveChangesAsync(cancellationToken);
        return purchase;
    }

    public async Task<SnAffiliationSpell?> FulfillRegistrationInvitePurchase(Guid purchaseId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var purchase = await db.AffiliationSpellPurchases.FirstOrDefaultAsync(x => x.Id == purchaseId && x.OrderId == orderId, cancellationToken);
        if (purchase is null || purchase.FulfilledAt is not null) return null;
        var spell = await CreateAffiliationSpell(purchase.AccountId, null);
        spell.Type = AffiliationSpellType.RegistrationInvite;
        purchase.SpellId = spell.Id;
        purchase.FulfilledAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);
        return spell;
    }

    public async Task<bool> ConsumeRegistrationInvite(string spellWord, Guid accountId, CancellationToken cancellationToken = default)
    {
        var spell = await db.AffiliationSpells.FirstOrDefaultAsync(x => x.Spell == spellWord && x.Type == AffiliationSpellType.RegistrationInvite, cancellationToken);
        if (spell is null || spell.AffectedAt is not null || (spell.ExpiresAt is not null && spell.ExpiresAt <= clock.GetCurrentInstant())) return false;
        spell.AffectedAt = clock.GetCurrentInstant();
        db.AffiliationResults.Add(new SnAffiliationResult { SpellId = spell.Id, ResourceIdentifier = $"account:{accountId}" });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SnAffiliationResult> CreateAffiliationResult(string spellWord, string resourceId)
    {
        return await RecordAffiliationEvent(spellWord, resourceId);
    }

    public async Task<bool> CheckAffiliationSpellHasTaken(string spellWord)
    {
        return await db.AffiliationSpells.AnyAsync(s => s.Spell == spellWord);
    }

    private static string _GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
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
