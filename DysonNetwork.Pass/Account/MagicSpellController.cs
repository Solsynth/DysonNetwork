using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/spells")]
public class MagicSpellController(AppDatabase db, MagicSpellService sp) : ControllerBase
{
    [HttpPost("{spellId:guid}/resend")]
    public async Task<ActionResult> ResendMagicSpell(Guid spellId)
    {
        var spell = db.MagicSpells.FirstOrDefault(x => x.Id == spellId);
        if (spell == null)
            return NotFound();
    
        await sp.NotifyMagicSpell(spell, true);
        return Ok();
    }
    
    [HttpGet("{spellWord}")]
    public async Task<ActionResult> GetMagicSpell(string spellWord)
    {
        var word = Uri.UnescapeDataString(spellWord);
        var spell = await db.MagicSpells
            .Where(x => x.Spell == word)
            .Include(x => x.Account)
            .ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync();
        if (spell is null)
            return NotFound();
        return Ok(spell);
    }

    public record class MagicSpellApplyRequest
    {
        public string? NewPassword { get; set; }
    }

    [HttpPost("{spellWord}/apply")]
    public async Task<ActionResult> ApplyMagicSpell([FromRoute] string spellWord, [FromBody] MagicSpellApplyRequest? request)
    {
        var word = Uri.UnescapeDataString(spellWord);
        var spell = await db.MagicSpells
            .Where(x => x.Spell == word)
            .Include(x => x.Account)
            .ThenInclude(x => x.Profile)
            .FirstOrDefaultAsync();
        if (spell is null)
            return NotFound();
        try
        {
            if (spell.Type == MagicSpellType.AuthPasswordReset && request?.NewPassword is not null)
                await sp.ApplyPasswordReset(spell, request.NewPassword);
            else
                await sp.ApplyMagicSpell(spell);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
        return Ok();
    }
}