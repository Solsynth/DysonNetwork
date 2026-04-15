using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SnAccountProfile = DysonNetwork.Shared.Models.SnAccountProfile;
using SnAccountPunishment = DysonNetwork.Shared.Models.SnAccountPunishment;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountPunishmentController(
    AppDatabase db,
    AccountService accounts,
    DyProfileService.DyProfileServiceClient profiles
) : ControllerBase
{
    [HttpGet("{name}/punishments")]
    public async Task<ActionResult<List<SnAccountPunishment>>> GetActivePunishments(
        string name,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null)
            return NotFound();

        var now = SystemClock.Instance.GetCurrentInstant();

        var query = db
            .Punishments.Where(p => p.AccountId == account.Id)
            .Where(p => p.ExpiredAt == null || p.ExpiredAt > now);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var punishments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        
        await accounts.HydratePunishmentAccountBatch(punishments);
        return Ok(punishments);
    }

    [HttpGet("me/punishments")]
    [Authorize]
    public async Task<ActionResult<AccountPunishmentResponse>> GetMyPunishments(
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var query = db.Punishments
            .Where(p => p.AccountId == currentUser.Id);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var punishments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        await accounts.HydratePunishmentAccountBatch(punishments);
        return Ok(
            new AccountPunishmentResponse { Account = currentUser, Punishments = punishments }
        );
    }

    public class AccountPunishmentResponse
    {
        public SnAccount? Account { get; set; }
        public List<SnAccountPunishment> Punishments { get; set; } = [];
    }
}

