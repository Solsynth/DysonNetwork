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
    NotableDaysService notableDaysService
) : ControllerBase
{
    public class RecoveryPasswordRequest
    {
        [Required]
        public string Account { get; set; } = null!;

        [Required]
        public string CaptchaToken { get; set; } = null!;
    }

    [HttpPost("recovery/password")]
    public async Task<ActionResult> RequestResetPassword([FromBody] RecoveryPasswordRequest request)
    {
        var captchaResp = await auth.ValidateCaptchaAsync(
            new DyValidateCaptchaRequest { Token = request.CaptchaToken }
        );
        if (!captchaResp.Valid)
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(request.CaptchaToken)] = ["Invalid captcha token."],
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );

        var account = await accounts.LookupAccount(request.Account);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "NOT_FOUND",
                    Message = "Unable to find the account.",
                    Detail = request.Account,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        try
        {
            await accounts.RequestPasswordReset(account);
        }
        catch (ArgumentException ex)
            when (ex.Message == "Account has no contact method that can use")
        {
            return BadRequest(
                new ApiError
                {
                    Code = "NO_CONTACT_METHOD",
                    Message = "This account has no email contact available for password reset.",
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );
        }
        catch (InvalidOperationException)
        {
            return BadRequest(
                new ApiError
                {
                    Code = "TOO_MANY_REQUESTS",
                    Message = "You already requested password reset within 24 hours.",
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );
        }

        return Ok();
    }

    public class StatusRequest
    {
        public StatusAttitude Attitude { get; set; }
        public StatusType Type { get; set; } = StatusType.Default;
        public bool IsAutomated { get; set; } = false;

        [MaxLength(1024)]
        public string? Label { get; set; }

        [MaxLength(128)]
        public string? Symbol { get; set; }

        [MaxLength(4096)]
        public string? AppIdentifier { get; set; }

        [MaxLength(32)]
        public string? IconId { get; set; }

        [MaxLength(32)]
        public string? BackgroundId { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Instant? ClearedAt { get; set; }
    }

    [HttpGet("{name}/statuses")]
    public async Task<ActionResult<SnAccountStatus>> GetOtherStatus(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "NOT_FOUND",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );
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
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(month)] = new[] { "Month must be between 1 and 12." },
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );
        if (year < 1)
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(year)] = new[] { "Year must be a positive integer." },
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );

        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "not_found",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        // Get viewer ID from current user if authenticated
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        // Use account's region with fallback to "us"
        var regionCode = account.Region;
        if (string.IsNullOrWhiteSpace(regionCode))
            regionCode = "us";

        var calendar = await events.GetEventCalendar(
            account,
            month.Value,
            year.Value,
            replaceInvisible: true,
            viewerId,
            includeNotableDays ? regionCode : null
        );

        // Add notable days if requested
        if (includeNotableDays)
        {
            var notableDays = await notableDaysService.GetNotableDays(year.Value, regionCode);
            var notableDaysByDate = notableDays
                .Where(d =>
                    d.Date.InUtc().Month == month.Value && d.Date.InUtc().Year == year.Value
                )
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
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(month)] = new[] { "Month must be between 1 and 12." },
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );
        if (year < 1)
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        [nameof(year)] = new[] { "Year must be a positive integer." },
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );

        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "not_found",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        // Get viewer ID from current user if authenticated
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        // Use account's region with fallback to "us"
        var regionCode = account.Region;
        if (string.IsNullOrWhiteSpace(regionCode))
            regionCode = "us";

        var calendar = await events.GetMergedEventCalendar(
            account,
            month.Value,
            year.Value,
            replaceInvisible: true,
            viewerId,
            regionCode,
            notableDaysService
        );

        return Ok(calendar);
    }

    [HttpGet("{name}/calendar/countdown")]
    public async Task<ActionResult<List<EventCountdownItem>>> GetOtherCountdown(
        string name,
        [FromQuery] int take = 5,
        [FromQuery] int offset = 0,
        [FromQuery] bool includeNotableDays = true,
        [FromQuery] string? tag = null
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "not_found",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        // Get viewer ID from current user if authenticated
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        // Use account's region with fallback to "us"
        var regionCode = account.Region;
        if (string.IsNullOrWhiteSpace(regionCode))
            regionCode = "us";

        NotableDayTag? tagFilter = null;
        if (
            !string.IsNullOrWhiteSpace(tag)
            && Enum.TryParse<NotableDayTag>(tag, true, out var parsedTag)
        )
        {
            tagFilter = parsedTag;
        }

        var (countdownItems, totalCount) = await events.GetCountdownEventsAsync(
            account,
            viewerId,
            regionCode,
            notableDaysService,
            includeNotableDays,
            tagFilter,
            take,
            offset
        );

        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(countdownItems);
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
            return BadRequest(
                new ApiError
                {
                    Code = "NOT_FOUND",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        var (timeline, total) = await events.GetTimeline(account.Id, offset, take);
        Response.Headers["X-Total"] = total.ToString();
        return Ok(timeline);
    }

    [HttpGet("{name}/calendar/events")]
    [ProducesResponseType<List<SnUserCalendarEvent>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnUserCalendarEvent>>> GetPublicCalendarEvents(
        string name,
        [FromQuery] Instant? startTime,
        [FromQuery] Instant? endTime,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "NOT_FOUND",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        var (userEvents, totalCount) = await events.GetUserCalendarEventsAsync(
            account.Id,
            viewerId,
            startTime,
            endTime,
            offset,
            take
        );

        foreach (var e in userEvents)
            e.Account = account;

        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(userEvents);
    }

    [HttpGet("{name}/calendar/events/{id:guid}")]
    [ProducesResponseType<SnUserCalendarEvent>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnUserCalendarEvent>> GetPublicCalendarEvent(
        string name,
        Guid id
    )
    {
        var account = await accounts.LookupAccount(name);
        if (account is null)
            return BadRequest(
                new ApiError
                {
                    Code = "NOT_FOUND",
                    Message = "Account not found.",
                    Detail = name,
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier,
                }
            );

        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        var calendarEvent = await events.GetCalendarEventAsync(id, viewerId);

        if (calendarEvent is null || calendarEvent.AccountId != account.Id)
            return NotFound(
                ApiError.NotFound("calendar event", traceId: HttpContext.TraceIdentifier)
            );

        calendarEvent.Account = account;
        return Ok(calendarEvent);
    }

    [HttpGet("unknown/calendar/events/{id:guid}")]
    [ProducesResponseType<SnUserCalendarEvent>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnUserCalendarEvent>> GetCalendarEventById(Guid id)
    {
        Guid? viewerId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
        {
            viewerId = currentUser.Id;
        }

        var calendarEvent = await events.GetCalendarEventAsync(id, viewerId);

        if (calendarEvent is null)
            return NotFound(
                ApiError.NotFound("calendar event", traceId: HttpContext.TraceIdentifier)
            );

        calendarEvent.Account = (await accounts.GetAccount(calendarEvent.AccountId))!;
        return Ok(calendarEvent);
    }
}
