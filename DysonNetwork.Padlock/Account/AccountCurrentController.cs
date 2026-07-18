using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Auth;
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
    public class PinStatusResponse
    {
        public bool HasPin { get; set; }
        public bool ValidationRequired { get; set; }
    }

    public class BasicInfoRequest
    {
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(32)] public string? Language { get; set; }
        [MaxLength(32)] public string? Region { get; set; }
    }

    [HttpPatch]
    [Authorize]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<ActionResult<SnAccount>> UpdateBasicInfo([FromBody] BasicInfoRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var account = await db.Accounts
            .Include(a => a.Profile)
            .Include(a => a.Badges)
            .FirstOrDefaultAsync(a => a.Id == currentUser.Id);

        if (account is null) return NotFound(new ApiError { Code = "PADLOCK_ACCOUNT_NOT_FOUND", Message = "Account not found.", Status = 404 });

        return Ok(account);
    }

    [HttpGet("pin-status")]
    public async Task<ActionResult<PinStatusResponse>> GetPinStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var hasPin = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id)
            .Where(f => f.Type == AccountAuthFactorType.PinCode)
            .Where(f => f.EnabledAt != null)
            .AnyAsync();

        return Ok(new PinStatusResponse
        {
            HasPin = hasPin,
            ValidationRequired = hasPin
        });
    }
}
