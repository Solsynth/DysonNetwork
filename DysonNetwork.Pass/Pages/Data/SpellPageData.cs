using DysonNetwork.Shared.PageData;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Pages.Data;

public class SpellPageData(AppDatabase db) : IPageDataProvider
{
    public bool CanHandlePath(PathString path) => path.StartsWithSegments("/spells");

    public async Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context)
    {
        var spellWord = context.Request.Path.Value!.Split('/').Last();
        spellWord = Uri.UnescapeDataString(spellWord);
        var now = SystemClock.Instance.GetCurrentInstant();
        var spell = await db.MagicSpells
            .Where(e => e.Spell == spellWord)
            .Where(e => e.ExpiresAt == null || now < e.ExpiresAt)
            .Where(e => e.AffectedAt == null || now >= e.AffectedAt)
            .Include(e => e.Account)
            .ThenInclude(e => e.Profile)
            .FirstOrDefaultAsync();
        return new Dictionary<string, object?>
        {
            ["Spell"] = spell
        };
    }
}