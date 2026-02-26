using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/bundles")]
public class BundleController(AppDatabase db) : ControllerBase
{
    public class BundleRequest
    {
        [MaxLength(1024)] public string? Slug { get; set; }
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(8192)] public string? Description { get; set; }
        [MaxLength(256)] public string? Passcode { get; set; }

        public Instant? ExpiredAt { get; set; }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnFileBundle>> GetBundle([FromRoute] Guid id, [FromQuery] string? passcode)
    {
        var bundle = await db.Bundles
            .Where(e => e.Id == id)
            .Include(e => e.Files)
            .FirstOrDefaultAsync();
        if (bundle is null) return NotFound();
        if (!bundle.VerifyPasscode(passcode)) return Forbid();

        return Ok(bundle);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<SnFileBundle>>> ListBundles(
        [FromQuery] string? term,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var query = db.Bundles
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.CreatedAt)
            .AsQueryable();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(e => EF.Functions.ILike(e.Name, $"%{term}%"));

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var bundles = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(bundles);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnFileBundle>> CreateBundle([FromBody] BundleRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        if (currentUser.PerkSubscription is null && !string.IsNullOrEmpty(request.Slug))
            return StatusCode(403, "You must have a subscription to create a bundle with a custom slug");
        if (string.IsNullOrEmpty(request.Slug))
            request.Slug = Guid.NewGuid().ToString("N")[..6];
        if (string.IsNullOrEmpty(request.Name))
            request.Name = "Unnamed Bundle";

        var bundle = new SnFileBundle
        {
            Slug = request.Slug,
            Name = request.Name,
            Description = request.Description,
            Passcode = request.Passcode,
            ExpiredAt = request.ExpiredAt,
            AccountId = accountId
        }.HashPasscode();

        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();

        return Ok(bundle);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnFileBundle>> UpdateBundle([FromRoute] Guid id, [FromBody] BundleRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var bundle = await db.Bundles
            .Where(e => e.Id == id)
            .Where(e => e.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (bundle is null) return NotFound();

        if (request.Slug != null && request.Slug != bundle.Slug)
        {
            if (currentUser.PerkSubscription is null)
                return StatusCode(403, "You must have a subscription to change the slug of a bundle");
            bundle.Slug = request.Slug;
        }

        if (request.Name != null) bundle.Name = request.Name;
        if (request.Description != null) bundle.Description = request.Description;
        if (request.ExpiredAt != null) bundle.ExpiredAt = request.ExpiredAt;

        if (request.Passcode != null)
        {
            bundle.Passcode = request.Passcode;
            bundle = bundle.HashPasscode();
        }

        await db.SaveChangesAsync();

        return Ok(bundle);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteBundle([FromRoute] Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var bundle = await db.Bundles
            .Where(e => e.Id == id)
            .Where(e => e.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (bundle is null) return NotFound();

        db.Bundles.Remove(bundle);
        await db.SaveChangesAsync();

        await db.Files
            .Where(e => e.BundleId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsMarkedRecycle, true));

        return NoContent();
    }
}
