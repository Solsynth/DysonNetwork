using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Pages.Spell;

public class MagicSpellPage(AppDatabase db, MagicSpellService spells) : PageModel
{
    [BindProperty]
    public MagicSpell? CurrentSpell { get; set; }

    public bool IsSuccess { get; set; }

    public async Task<IActionResult> OnGetAsync(string spellWord)
    {
        spellWord = Uri.UnescapeDataString(spellWord);
        var now = SystemClock.Instance.GetCurrentInstant();
        CurrentSpell = await db.MagicSpells
            .Where(e => e.Spell == spellWord)
            .Where(e => e.ExpiresAt == null || now < e.ExpiresAt)
            .Where(e => e.AffectedAt == null || now >= e.AffectedAt)
            .Include(e => e.Account)
            .FirstOrDefaultAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CurrentSpell?.Id == null)
            return Page();

        var now = SystemClock.Instance.GetCurrentInstant();
        var spell = await db.MagicSpells
            .Where(e => e.Id == CurrentSpell.Id)
            .Where(e => e.ExpiresAt == null || now < e.ExpiresAt)
            .Where(e => e.AffectedAt == null || now >= e.AffectedAt)
            .FirstOrDefaultAsync();

        if (spell == null)
            return Page();

        await spells.ApplyMagicSpell(spell);
        IsSuccess = true;
        return Page();
    }
}