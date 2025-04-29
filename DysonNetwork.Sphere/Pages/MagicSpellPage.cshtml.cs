using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Pages;

public class MagicSpellPage(AppDatabase db, MagicSpellService spells) : PageModel
{
    public async Task<ActionResult> OnGet(string spellWord)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var spell = await db.MagicSpells
            .Where(e => e.Spell == spellWord)
            .Where(e => e.ExpiresAt == null || now >= e.ExpiresAt)
            .Where(e => e.AffectedAt == null || now >= e.AffectedAt)
            .FirstOrDefaultAsync();
        
        ViewData["Spell"] = spell;

        return Page();
    }
}