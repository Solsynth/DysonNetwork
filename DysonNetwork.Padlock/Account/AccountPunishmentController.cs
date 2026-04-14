using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SnAccountPunishment = DysonNetwork.Shared.Models.SnAccountPunishment;
using SnAccountProfile = DysonNetwork.Shared.Models.SnAccountProfile;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/accounts")]
public class AccountPunishmentController(
    AppDatabase db,
    AccountService accounts,
    DyProfileService.DyProfileServiceClient profiles
) : ControllerBase
{
    [HttpGet("{name}/punishments")]
    public async Task<ActionResult<List<SnAccountPunishment>>> GetActivePunishments(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var now = SystemClock.Instance.GetCurrentInstant();
        var punishments = await db.Punishments
            .Where(p => p.AccountId == account.Id)
            .Where(p => p.ExpiredAt == null || p.ExpiredAt > now)
            .ToListAsync();
        return Ok(punishments);
    }

    [HttpGet("me/punishments")]
    [Authorize]
    public async Task<ActionResult<AccountPunishmentResponse>> GetMyPunishments()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var remoteAccount = await profiles.GetAccountAsync(new DyGetAccountRequest { Id = currentUser.Id.ToString() });
        if (remoteAccount is not null)
        {
            currentUser.Language = remoteAccount.Language;
            currentUser.Profile = remoteAccount.Profile is not null ? SnAccountProfile.FromProtoValue(remoteAccount.Profile) : null;
        }

        var punishments = await db.Punishments
            .Where(p => p.AccountId == currentUser.Id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return Ok(new AccountPunishmentResponse { Account = currentUser, Punishments = punishments });
    }

    public class AccountPunishmentResponse
    {
        public SnAccount? Account { get; set; }
        public List<SnAccountPunishment> Punishments { get; set; } = [];
    }
}