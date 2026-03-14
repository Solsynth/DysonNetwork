using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Account;

[Authorize]
[ApiController]
[Route("/api/accounts/me")]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts
) : ControllerBase
{
    public class BasicInfoRequest
    {
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(32)] public string? Language { get; set; }
        [MaxLength(32)] public string? Region { get; set; }
    }

    [HttpPatch]
    public async Task<ActionResult<SnAccount>> UpdateBasicInfo([FromBody] BasicInfoRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var updatedAccount = await accounts.UpdateBasicInfo(
            currentUser,
            request.Nick,
            request.Language,
            request.Region
        );

        return Ok(updatedAccount);
    }

    [HttpGet]
    public async Task<ActionResult<SnAccount>> GetCurrentAccount()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await db.Accounts
            .Include(a => a.Profile)
            .Include(a => a.Badges)
            .FirstOrDefaultAsync(a => a.Id == currentUser.Id);

        if (account is null) return NotFound();

        return Ok(account);
    }
}
