using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/spells")]
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
}