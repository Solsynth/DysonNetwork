using System.Security.Cryptography;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Affiliation;

public class AffiliationSpellService(AppDatabase db)
{
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

    public async Task<SnAffiliationResult> CreateAffiliationResult(string spellWord, string resourceId)
    {
        var spell =
            await db.AffiliationSpells.FirstOrDefaultAsync(a => a.Spell == spellWord);
        if (spell is null) throw  new InvalidOperationException("The spell was not found.");

        var result = new SnAffiliationResult
        {
            Spell = spell,
            ResourceIdentifier = resourceId
        };
        db.AffiliationResults.Add(result);
        await db.SaveChangesAsync();
        
        return result;
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