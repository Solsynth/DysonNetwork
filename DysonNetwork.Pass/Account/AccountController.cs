using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Error;
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
    SubscriptionService subscriptions,
    AccountEventService events,
    SocialCreditService socialCreditService
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
            .Include(e => e.Contacts.Where(c => c.IsPublic))
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();
        if (account is null) return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));
        
        var perk = await subscriptions.GetPerkSubscriptionAsync(account.Id);
        account.PerkSubscription = perk?.ToReference();
        
        return account;
    }

    [HttpGet("{name}/badges")]
    [ProducesResponseType<List<AccountBadge>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AccountBadge>>> GetBadgesByName(string name)
    {
        var account = await db.Accounts
            .Include(e => e.Badges)
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();
        return account is null ? NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier)) : account.Badges.ToList();
    }
    
    [HttpGet("{name}/credits")]
    [ProducesResponseType<double>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<double>> GetSocialCredits(string name)
    {
        var account = await db.Accounts
            .Where(a => a.Name == name)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync();
            
        if (account is null)
        {
            return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));
        }
        
        var credits = await socialCreditService.GetSocialCredit(account.Id);
        return credits;
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
        if (!await auth.ValidateCaptcha(request.CaptchaToken))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(request.CaptchaToken)] = ["Invalid captcha token."]
            }, traceId: HttpContext.TraceIdentifier));

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
        [MaxLength(1024)] public string? Label { get; set; }
        public Instant? ClearedAt { get; set; }
    }

    [HttpGet("{name}/statuses")]
    public async Task<ActionResult<Status>> GetOtherStatus(string name)
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
}