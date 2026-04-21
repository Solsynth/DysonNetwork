using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountController(
    DyAuthService.DyAuthServiceClient auth,
    AccountService accounts,
    AccountEventService events,
    AppDatabase db,
    NotableDaysService notableDaysService
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
        var captchaResp =
            await auth.ValidateCaptchaAsync(new DyValidateCaptchaRequest { Token = request.CaptchaToken });
        if (!captchaResp.Valid)
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
        public StatusType Type { get; set; } = StatusType.Default;
        public bool IsAutomated { get; set; } = false;
        [MaxLength(1024)] public string? Label { get; set; }
        [MaxLength(128)] public string? Symbol { get; set; }
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
        if (status.Type == StatusType.Invisible)
            status.Type = StatusType.Default;
        return Ok(status);
    }

    [HttpGet("{name}/calendar")]
    public async Task<ActionResult<List<DailyEventResponse>>> GetOtherEventCalendar(
        string name,
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] bool includeNotableDays = false
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

        // Get viewer ID from current user if authenticated
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        // Get region code for notable days
        string? regionCode = null;
        if (includeNotableDays)
        {
            var profile = await db.AccountProfiles
                .Where(p => p.AccountId == account.Id)
                .Select(p => new { p.Location })
                .FirstOrDefaultAsync();
            regionCode = profile?.Location;
        }

        var calendar = await events.GetEventCalendar(
            account,
            month.Value,
            year.Value,
            replaceInvisible: true,
            viewerId,
            includeNotableDays ? regionCode : null);

        // Add notable days if requested
        if (includeNotableDays && !string.IsNullOrWhiteSpace(regionCode))
        {
            var notableDays = await notableDaysService.GetNotableDays(year.Value, regionCode);
            var notableDaysByDate = notableDays
                .Where(d => d.Date.InUtc().Month == month.Value && d.Date.InUtc().Year == year.Value)
                .GroupBy(d => d.Date.InUtc().Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var day in calendar)
            {
                var utcDate = day.Date.InUtc().Date;
                if (notableDaysByDate.TryGetValue(utcDate, out var days))
                {
                    day.NotableDays = days;
                }
            }
        }

        return Ok(calendar);
    }

    [HttpGet("{name}/calendar/merged")]
    public async Task<ActionResult<MergedDailyEventResponse>> GetOtherMergedEventCalendar(
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

        // Get viewer ID from current user if authenticated
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        // Get region code for notable days
        var profile = await db.AccountProfiles
            .Where(p => p.AccountId == account.Id)
            .Select(p => new { p.Location })
            .FirstOrDefaultAsync();
        var regionCode = profile?.Location;

        var calendar = await events.GetMergedEventCalendar(
            account,
            month.Value,
            year.Value,
            replaceInvisible: true,
            viewerId,
            regionCode,
            notableDaysService);

        return Ok(calendar);
    }

    [HttpGet("{name}/timeline")]
    public async Task<ActionResult<List<AccountTimelineItem>>> GetTimeline(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
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

        var (timeline, total) = await events.GetTimeline(account.Id, offset, take);
        Response.Headers["X-Total"] = total.ToString();
        return Ok(timeline);
    }
}
