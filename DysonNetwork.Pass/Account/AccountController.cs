using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Affiliation;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountController(
    AppDatabase db,
    AuthService auth,
    AccountService accounts,
    AccountEventService events,
    SocialCreditService socialCreditService,
    AffiliationSpellService ars,
    GeoService geo
) : ControllerBase
{
    public class AccountCreateRequest
    {
        [Required]
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")
        ]
        public string Name { get; set; } = string.Empty;

        [Required][MaxLength(256)] public string Nick { get; set; } = string.Empty;

        [EmailAddress]
        [RegularExpression(@"^[^+]+@[^@]+\.[^@]+$", ErrorMessage = "Email address cannot contain '+' symbol.")]
        [Required]
        [MaxLength(1024)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(4)]
        [MaxLength(128)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(32)] public string Language { get; set; } = "en-us";

        [Required] public string CaptchaToken { get; set; } = string.Empty;

        public string? AffiliationSpell { get; set; }
    }

    public class AccountCreateValidateRequest
    {
        [MinLength(2)]
        [MaxLength(256)]
        [RegularExpression(@"^[A-Za-z0-9_-]+$",
            ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens.")
        ]
        public string? Name { get; set; }

        [EmailAddress]
        [RegularExpression(@"^[^+]+@[^@]+\.[^@]+$", ErrorMessage = "Email address cannot contain '+' symbol.")]
        [MaxLength(1024)]
        public string? Email { get; set; }

        public string? AffiliationSpell { get; set; }
    }

    [HttpPost("validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<string>> ValidateCreateAccountRequest(
        [FromBody] AccountCreateValidateRequest request)
    {
        if (request.Name is not null)
        {
            if (await accounts.CheckAccountNameHasTaken(request.Name))
                return BadRequest("Account name has already been taken.");
        }

        if (request.Email is not null)
        {
            if (await accounts.CheckEmailHasBeenUsed(request.Email))
                return BadRequest("Email has already been used.");
        }

        if (request.AffiliationSpell is not null)
        {
            if (!await ars.CheckAffiliationSpellHasTaken(request.AffiliationSpell))
                return BadRequest("No affiliation spell has been found.");
        }

        return Ok("Everything seems good.");
    }

    [HttpPost]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnAccount>> CreateAccount([FromBody] AccountCreateRequest request)
    {
        if (!await auth.ValidateCaptcha(request.CaptchaToken))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.CaptchaToken)] = ["Invalid captcha token."]
            }, traceId: HttpContext.TraceIdentifier));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (ip is null) return BadRequest(ApiError.NotFound(request.Name, traceId: HttpContext.TraceIdentifier));
        var region = geo.GetFromIp(ip)?.Country.IsoCode ?? "us";

        try
        {
            var account = await accounts.CreateAccount(
                request.Name,
                request.Nick,
                request.Email,
                request.Password,
                request.Language,
                region
            );
            return Ok(account);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "Failed to create account.",
                Detail = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
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
        if (!await auth.ValidateCaptcha(request.CaptchaToken))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.CaptchaToken)] = new[] { "Invalid captcha token." }
            }, traceId: HttpContext.TraceIdentifier));

        var account = await accounts.LookupAccount(request.Account);
        if (account is null)
            return BadRequest(new ApiError
            {
                Code = "NOT_FOUND",
                Message = "Unable to find the account.",
                Detail = request.Account,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });

        try
        {
            await accounts.RequestPasswordReset(account);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new ApiError
            {
                Code = "TOO_MANY_REQUESTS",
                Message = "You already requested password reset within 24 hours.",
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok();
    }

    public class StatusRequest
    {
        public StatusAttitude Attitude { get; set; }
        public bool IsInvisible { get; set; }
        public bool IsNotDisturb { get; set; }
        public bool IsAutomated { get; set; } = false;
        [MaxLength(1024)] public string? Label { get; set; }
        [MaxLength(4096)] public string? AppIdentifier { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Instant? ClearedAt { get; set; }
    }

    [HttpGet("{name}/statuses")]
    public async Task<ActionResult<SnAccountStatus>> GetOtherStatus(string name)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
        if (account is null)
            return BadRequest(new ApiError
            {
                Code = "NOT_FOUND",
                Message = "Account not found.",
                Detail = name,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
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

        if (month is < 1 or > 12)
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(month)] = new[] { "Month must be between 1 and 12." }
            }, traceId: HttpContext.TraceIdentifier));
        if (year < 1)
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(year)] = new[] { "Year must be a positive integer." }
            }, traceId: HttpContext.TraceIdentifier));

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
        if (account is null)
            return BadRequest(new ApiError
            {
                Code = "not_found",
                Message = "Account not found.",
                Detail = name,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });

        var calendar = await events.GetEventCalendar(account, month.Value, year.Value, replaceInvisible: true);
        return Ok(calendar);
    }
    
    [HttpGet("{name}/punishments")]
    public async Task<ActionResult<List<DailyEventResponse>>> GetPunishments(string name)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == name);
        if (account is null) return NotFound();

        var punishments = await db.Punishments
            .Where(a => a.AccountId == account.Id)
            .ToListAsync();
        return Ok(punishments);
    }

    [HttpPost("credits/invalidate-cache")]
    [Authorize]
    [AskPermission("credits.validate.perform")]
    public async Task<IActionResult> InvalidateSocialCreditCache()
    {
        await socialCreditService.InvalidateCache();
        return Ok();
    }

    [HttpDelete("{name}")]
    [Authorize]
    [AskPermission("accounts.deletion")]
    public async Task<IActionResult> AdminDeleteAccount(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();
        await accounts.DeleteAccount(account);
        return Ok();
    }
}
