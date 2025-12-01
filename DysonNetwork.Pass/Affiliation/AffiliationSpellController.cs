using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Affiliation;

[ApiController]
[Route("/api/affiliations")]
public class AffiliationSpellController(AppDatabase db, AffiliationSpellService ars) : ControllerBase
{
    public class CreateAffiliationSpellRequest
    {
        [MaxLength(1024)] public string? Spell { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnAffiliationSpell>> CreateSpell([FromBody] CreateAffiliationSpellRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var spell = await ars.CreateAffiliationSpell(currentUser.Id, request.Spell);
            return Ok(spell);
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(e.Message);
        }
    }
    
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnAffiliationSpell>>> ListCreatedSpells(
        [FromQuery(Name = "order")] string orderBy = "date",
        [FromQuery(Name = "desc")] bool orderDesc = false,
        [FromQuery] int take = 10,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.AffiliationSpells
            .Where(s => s.AccountId == currentUser.Id)
            .AsQueryable();

        queryable = orderBy switch
        {
            "usage" => orderDesc
                ? queryable.OrderByDescending(q => q.Results.Count)
                : queryable.OrderBy(q => q.Results.Count),
            _ => orderDesc
                ? queryable.OrderByDescending(q => q.CreatedAt)
                : queryable.OrderBy(q => q.CreatedAt)
        };

        var totalCount = queryable.Count();
        Response.Headers["X-Total"] = totalCount.ToString();

        var spells = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(spells);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAffiliationSpell>> GetSpell([FromRoute] Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var spell = await db.AffiliationSpells
            .Where(s => s.AccountId == currentUser.Id)
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (spell is null) return NotFound();

        return Ok(spell);
    }

    [HttpGet("{id:guid}/results")]
    [Authorize]
    public async Task<ActionResult<List<SnAffiliationResult>>> ListResults(
        [FromRoute] Guid id,
        [FromQuery(Name = "desc")] bool orderDesc = false,
        [FromQuery] int take = 10,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.AffiliationResults
            .Include(r => r.Spell)
            .Where(r => r.Spell.AccountId == currentUser.Id)
            .Where(r => r.SpellId == id)
            .AsQueryable();

        // Order by creation date
        queryable = orderDesc
            ? queryable.OrderByDescending(r => r.CreatedAt)
            : queryable.OrderBy(r => r.CreatedAt);

        var totalCount = queryable.Count();
        Response.Headers["X-Total"] = totalCount.ToString();

        var results = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(results);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteSpell([FromRoute] Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var spell = await db.AffiliationSpells
            .Where(s => s.AccountId == currentUser.Id)
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();
        if (spell is null) return NotFound();

        db.AffiliationSpells.Remove(spell);
        await db.SaveChangesAsync();

        return Ok();
    }
}
