using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;
using System.Collections.Generic;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/accounts")]
public class AccountController(
    AppDatabase db,
    AuthService auth,
    AccountService accounts,
    AccountEventService events
) : ControllerBase
{
    [HttpGet("{name}")]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Account?>> GetByName(string name)
    {
        var account = await db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();
        return account is null ? new NotFoundResult() : account;
    }

    [HttpGet("{name}/badges")]
    [ProducesResponseType<List<Badge>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Badge>>> GetBadgesByName(string name)
    {
        var account = await db.Accounts
            .Include(e => e.Badges)
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();
        return account is null ? NotFound() : account.Badges.ToList();
    }

    public class AccountCreateRequest
    {
        [Required]
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")
        ]
        public string Name { get; set; } = string.Empty;

        [Required] [MaxLength(256)] public string Nick { get; set; } = string.Empty;

        [EmailAddress]
        [RegularExpression(@"^[^+]+@[^@]+\.[^@]+$", ErrorMessage = "Email address cannot contain '+' symbol.")]
        [Required]
        [MaxLength(1024)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [MaxLength(128)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(128)] public string Language { get; set; } = "en-us";

        [Required] public string CaptchaToken { get; set; } = string.Empty;
    }

    [HttpPost]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Account>> CreateAccount([FromBody] AccountCreateRequest request)
    {
        if (!await auth.ValidateCaptcha(request.CaptchaToken)) return BadRequest("Invalid captcha token.");

        try
        {
            var account = await accounts.CreateAccount(
                request.Name,
                request.Nick,
                request.Email,
                request.Password,
                request.Language
            );
            return Ok(account);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public class RecoveryPasswordRequest
    {
        [Required] public string Account { get; set; } = null!;
        [Required] public string CaptchaToken { get; set; } = null!;
    }

    [HttpPost("recovery/password")]
    public async Task<ActionResult> RequestResetPassword([FromBody] RecoveryPasswordRequest request)
    {
        if (!await auth.ValidateCaptcha(request.CaptchaToken)) return BadRequest("Invalid captcha token.");

        var account = await accounts.LookupAccount(request.Account);
        if (account is null) return BadRequest("Unable to find the account.");

        try
        {
            await accounts.RequestPasswordReset(account);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("You already requested password reset within 24 hours.");
        }

        return Ok();
    }

    public class StatusRequest
    {
        public StatusAttitude Attitude { get; set; }
        public bool IsInvisible { get; set; }
        public bool IsNotDisturb { get; set; }
        [MaxLength(1024)] public string? Label { get; set; }
        public Instant? ClearedAt { get; set; }
    }

    [HttpGet("{name}/statuses")]
    public async Task<ActionResult<Status>> GetOtherStatus(string name)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
        if (account is null) return BadRequest();
        var status = await events.GetStatus(account.Id);
        status.IsInvisible = false; // Keep the invisible field not available for other users
        return Ok(status);
    }

    [HttpGet("{name}/calendar")]
    public async Task<ActionResult<List<DailyEventResponse>>> GetOtherEventCalendar(
        string name,
        [FromQuery] int? month,
        [FromQuery] int? year
    )
    {
        var currentDate = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        month ??= currentDate.Month;
        year ??= currentDate.Year;

        if (month is < 1 or > 12) return BadRequest("Invalid month.");
        if (year < 1) return BadRequest("Invalid year.");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
        if (account is null) return BadRequest();

        var calendar = await events.GetEventCalendar(account, month.Value, year.Value, replaceInvisible: true);
        return Ok(calendar);
    }

    [HttpGet("search")]
    public async Task<List<Account>> Search([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        return await db.Accounts
            .Include(e => e.Profile)
            .Where(a => EF.Functions.ILike(a.Name, $"%{query}%") ||
                        EF.Functions.ILike(a.Nick, $"%{query}%"))
            .Take(take)
            .ToListAsync();
    }

    [HttpPost("/maintenance/ensureProfileCreated")]
    [Authorize]
    [RequiredPermission("maintenance", "accounts.profiles")]
    public async Task<ActionResult> EnsureProfileCreated()
    {
        await accounts.EnsureAccountProfileCreated();
        return Ok();
    }
}