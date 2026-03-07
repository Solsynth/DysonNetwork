using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountController(
    AccountService accounts,
    AccountEventService events
) : ControllerBase
{
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
                [nameof(request.CaptchaToken)] = ["Invalid captcha token."]
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
        var account = await accounts.LookupAccount(name);
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

        var account = await accounts.LookupAccount(name);
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
}
