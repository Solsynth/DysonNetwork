using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/spells")]
[ApiFeature("spells", Revision = 1)]
public class MagicSpellController(
    AppDatabase db,
    MagicSpellService sp,
    DyAccountService.DyAccountServiceClient accountGrpc
) : ControllerBase
{
    [HttpPost("activation/resend")]
    [Authorize]
    public async Task<ActionResult> ResendActivationMagicSpell()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var spell = await db.MagicSpells.FirstOrDefaultAsync(s =>
            s.Type == MagicSpellType.AccountActivation && s.AccountId == currentUser.Id);
        if (spell is null) return BadRequest(new ApiError { Code = "PASSPORT_SPELL_NOT_FOUND", Message = "Unable to find activation magic spell.", Status = 400, TraceId = HttpContext.TraceIdentifier });
        
        await sp.NotifyMagicSpell(spell, true);
        return Ok();
    }

    [HttpPost("{spellId:guid}/resend")]
    public async Task<ActionResult> ResendMagicSpell(Guid spellId)
    {
        var spell = db.MagicSpells.FirstOrDefault(x => x.Id == spellId);
        if (spell == null)
            return NotFound(new ApiError { Code = "PASSPORT_SPELL_NOT_FOUND", Message = "Magic spell not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        await sp.NotifyMagicSpell(spell, true);
        return Ok();
    }

    [HttpGet("{spellWord}")]
    public async Task<ActionResult> GetMagicSpell(string spellWord)
    {
        var word = Uri.UnescapeDataString(spellWord);
        var spell = await db.MagicSpells
            .Where(x => x.Spell == word)
            .FirstOrDefaultAsync();
        if (spell is null)
            return NotFound(new ApiError { Code = "PASSPORT_SPELL_NOT_FOUND", Message = "Magic spell not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        if (spell.AccountId.HasValue)
        {
            try
            {
                var account = await accountGrpc.GetAccountAsync(new DyGetAccountRequest
                {
                    Id = spell.AccountId.Value.ToString()
                });
                spell.Account = SnAccount.FromProtoValue(account);
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                spell.Account = null;
            }
        }

        return Ok(spell);
    }

    public record class MagicSpellApplyRequest
    {
        public string? NewPassword { get; set; }
    }

    [HttpPost("{spellWord}/apply")]
    public async Task<ActionResult> ApplyMagicSpell([FromRoute] string spellWord,
        [FromBody] MagicSpellApplyRequest? request)
    {
        var word = Uri.UnescapeDataString(spellWord);
        var spell = await db.MagicSpells
            .Where(x => x.Spell == word)
            .FirstOrDefaultAsync();
        if (spell is null)
            return NotFound(new ApiError { Code = "PASSPORT_SPELL_NOT_FOUND", Message = "Magic spell not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });
        try
        {
            if (spell.Type == MagicSpellType.AuthPasswordReset && request?.NewPassword is not null)
                await sp.ApplyPasswordReset(spell, request.NewPassword);
            else
                await sp.ApplyMagicSpell(spell);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_SPELL_APPLY_FAILED", Message = ex.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }

        return Ok();
    }
}
