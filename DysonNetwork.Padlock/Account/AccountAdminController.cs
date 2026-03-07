using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    AppDatabase db,
    AccountService accounts
) : ControllerBase
{
    [HttpGet("{name}/punishments")]
    public async Task<ActionResult<List<SnAccountPunishment>>> GetPunishments(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishments = await db.Punishments
            .Where(a => a.AccountId == account.Id)
            .ToListAsync();
        return Ok(punishments);
    }

    public class CreatePunishmentRequest
    {
        [MaxLength(8192)] public string Reason { get; set; } = string.Empty;
        public Instant? ExpiredAt { get; set; }
        public PunishmentType Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
    }

    [HttpPost("{name}/punishments")]
    [AskPermission("punishments.create")]
    public async Task<ActionResult<List<SnAccountPunishment>>> CreatePunishment(
        string name,
        [FromBody] CreatePunishmentRequest request
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = new SnAccountPunishment
        {
            AccountId = account.Id,
            Reason = request.Reason,
            ExpiredAt = request.ExpiredAt,
            Type = request.Type,
            BlockedPermissions = request.BlockedPermissions
        };

        db.Punishments.Add(punishment);
        await db.SaveChangesAsync();

        var punishments = await db.Punishments
            .Where(p => p.AccountId == account.Id)
            .ToListAsync();
        return Ok(punishments);
    }

    public class UpdatePunishmentRequest
    {
        [MaxLength(8192)] public string? Reason { get; set; }
        public Instant? ExpiredAt { get; set; }
        public PunishmentType? Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
    }

    [HttpPatch("{name}/punishments/{punishmentId}")]
    [AskPermission("punishments.update")]
    public async Task<ActionResult<SnAccountPunishment>> UpdatePunishment(
        string name,
        Guid punishmentId,
        [FromBody] UpdatePunishmentRequest request
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        if (request.Reason is not null) punishment.Reason = request.Reason;
        if (request.ExpiredAt is not null) punishment.ExpiredAt = request.ExpiredAt;
        if (request.Type is not null) punishment.Type = request.Type.Value;
        if (request.BlockedPermissions is not null) punishment.BlockedPermissions = request.BlockedPermissions;

        await db.SaveChangesAsync();
        return Ok(punishment);
    }

    [HttpDelete("{name}/punishments/{punishmentId}")]
    [AskPermission("punishments.delete")]
    public async Task<ActionResult> DeletePunishment(string name, Guid punishmentId)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        db.Punishments.Remove(punishment);
        await db.SaveChangesAsync();
        return Ok();
    }
    
    [HttpDelete("{name}")]
    [AskPermission("accounts.deletion")]
    public async Task<IActionResult> AdminDeleteAccount(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();
        await accounts.DeleteAccount(account);
        return Ok();
    }
}
