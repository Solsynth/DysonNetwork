using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using System.Text.Json;

namespace DysonNetwork.Passport.Account;

[Authorize]
[ApiController]
[Route("/api/accounts/me")]
public class AccountEventController(
    AppDatabase db,
    AccountService accounts,
    AccountEventService events,
    DyFileService.DyFileServiceClient files,
    RemoteSubscriptionService remoteSubscription,
    RemoteActionLogService remoteActionLogs,
    DyAuthService.DyAuthServiceClient auth
) : ControllerBase
{
    [HttpGet("statuses")]
    public async Task<ActionResult<SnAccountStatus>> GetCurrentStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var status = await events.GetStatus(currentUser.Id);
        return Ok(status);
    }

    [HttpPatch("statuses")]
    [AskPermission("accounts.statuses.update")]
    public async Task<ActionResult<SnAccountStatus>> UpdateStatus([FromBody] AccountController.StatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (request is { IsAutomated: true, AppIdentifier: not null })
            return BadRequest("Automated status cannot be updated.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var status = await db.AccountStatuses
            .Where(e => e.AccountId == currentUser.Id)
            .Where(e => e.ClearedAt == null || e.ClearedAt > now)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        if (status is null) return NotFound(ApiError.NotFound("status", traceId: HttpContext.TraceIdentifier));
        if (status.IsAutomated && request.AppIdentifier is null)
            return BadRequest("Automated status cannot be updated.");

        status.Attitude = request.Attitude;
        status.Type = request.Type;
        status.IsAutomated = request.IsAutomated;
        status.Label = request.Label;
        status.Symbol = request.Symbol;
        status.AppIdentifier = request.AppIdentifier;
        status.Meta = request.Meta;
        status.ClearedAt = request.ClearedAt;

        if (request.IconId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
            if (file is null)
                return BadRequest("Icon not found.");
            status.Icon = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (file is null)
                return BadRequest("Background not found.");
            status.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        db.Update(status);
        await db.SaveChangesAsync();
        events.PurgeStatusCache(currentUser.Id);

        return status;
    }

    [HttpPost("statuses")]
    [AskPermission("accounts.statuses.create")]
    public async Task<ActionResult<SnAccountStatus>> CreateStatus([FromBody] AccountController.StatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        if (request is { IsAutomated: true, AppIdentifier: not null })
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var existingStatus = await db.AccountStatuses
                .Where(s => s.AccountId == currentUser.Id)
                .Where(s => s.ClearedAt == null || s.ClearedAt > now)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
            if (existingStatus is not null && existingStatus.IsAutomated)
                if (existingStatus.IsAutomated && request.AppIdentifier == existingStatus.AppIdentifier)
                {
                    existingStatus.Attitude = request.Attitude;
                    existingStatus.Type = request.Type;
                    existingStatus.IsAutomated = request.IsAutomated;
                    existingStatus.Meta = request.Meta;
                    existingStatus.Label = request.Label;
                    existingStatus.Symbol = request.Symbol;
                    existingStatus.AppIdentifier = request.AppIdentifier;
                    existingStatus.ClearedAt = request.ClearedAt;

                    if (request.IconId is not null)
                    {
                        var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
                        if (file is not null)
                            existingStatus.Icon = SnCloudFileReferenceObject.FromProtoValue(file);
                    }

                    if (request.BackgroundId is not null)
                    {
                        var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
                        if (file is not null)
                            existingStatus.Background = SnCloudFileReferenceObject.FromProtoValue(file);
                    }

                    db.Update(existingStatus);
                    await db.SaveChangesAsync();
                    events.PurgeStatusCache(currentUser.Id);
                    return Ok(existingStatus);
                }
                else
                {
                    existingStatus.ClearedAt = now;
                    db.Update(existingStatus);
                    await db.SaveChangesAsync();
                }
            else if (existingStatus is not null)
                return Ok(existingStatus); // Do not override manually set status with automated ones
        }

        var status = new SnAccountStatus
        {
            AccountId = currentUser.Id,
            Attitude = request.Attitude,
            Type = request.Type,
            IsAutomated = request.IsAutomated,
            Label = request.Label,
            Symbol = request.Symbol,
            Meta = request.Meta,
            AppIdentifier = request.AppIdentifier,
            ClearedAt = request.ClearedAt
        };

        if (request.IconId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
            if (file is not null)
                status.Icon = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (file is not null)
                status.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        return await events.CreateStatus(currentUser, status);
    }

    [HttpDelete("statuses")]
    public async Task<ActionResult> DeleteStatus([FromQuery] string? app)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var now = SystemClock.Instance.GetCurrentInstant();
        var queryable = db.AccountStatuses
            .Where(s => s.AccountId == currentUser.Id)
            .Where(s => s.ClearedAt == null || s.ClearedAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(app))
            queryable = queryable.Where(s => s.IsAutomated && s.AppIdentifier == app);

        var status = await queryable
            .FirstOrDefaultAsync();
        if (status is null) return NotFound();

        await events.ClearStatus(currentUser, status);
        return NoContent();
    }

    [HttpGet("check-in")]
    public async Task<ActionResult<SnCheckInResult>> GetCheckInResult([FromQuery] int version = 1)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var now = SystemClock.Instance.GetCurrentInstant();
        var today = now.InUtc().Date;
        var startOfDay = today.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var endOfDay = today.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

        var result = await db.AccountCheckInResults
            .Where(x => x.AccountId == userId)
            .Where(x => x.CreatedAt >= startOfDay && x.CreatedAt < endOfDay)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (result is null)
            return NotFound(ApiError.NotFound("check-in", traceId: HttpContext.TraceIdentifier));

        events.PrepareCheckInResultForResponse(currentUser, result, version);

        return Ok(result);
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<SnCheckInResult>> DoCheckIn(
        [FromBody] string? captchaToken,
        [FromQuery] Instant? backdated = null,
        [FromQuery] int version = 1
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        if (backdated is null)
        {
            var isAvailable = await events.CheckInDailyIsAvailable(currentUser);
            if (!isAvailable)
                return BadRequest(new ApiError
                {
                    Code = "BAD_REQUEST",
                    Message = "Check-in is not available for today.",
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier
                });
        }
        else
        {
            // Check PerkSubscription via RemoteSubscriptionService instead of relying on currentUser.PerkSubscription
            // which is not populated when currentUser comes from HttpContext.Items
            var perkSubscription = await remoteSubscription.GetPerkSubscription(currentUser.Id);
            if (perkSubscription is null)
                return StatusCode(403, ApiError.Unauthorized(
                    message: "You need to have a subscription to check-in backdated.",
                    forbidden: true,
                    traceId: HttpContext.TraceIdentifier));
            var isAvailable = await events.CheckInBackdatedIsAvailable(currentUser, backdated.Value);
            if (!isAvailable)
                return BadRequest(new ApiError
                {
                    Code = "BAD_REQUEST",
                    Message = "Check-in is not available for this date.",
                    Status = 400,
                    TraceId = HttpContext.TraceIdentifier
                });
        }

        try
        {
            var needsCaptcha = await events.CheckInDailyDoAskCaptcha(currentUser);
            if (needsCaptcha)
            {
                if (string.IsNullOrWhiteSpace(captchaToken))
                    return StatusCode(423,
                        new ApiError
                        {
                            Code = "CAPTCHA_REQUIRED",
                            Message = "Captcha is required for this check-in.",
                            Status = 423,
                            TraceId = HttpContext.TraceIdentifier
                        }
                    );
                var captchaResp =
                    await auth.ValidateCaptchaAsync(new DyValidateCaptchaRequest { Token = captchaToken });
                if (!captchaResp.Valid)
                    return BadRequest(ApiError.Validation(
                        new Dictionary<string, string[]>
                        {
                            ["captchaToken"] = ["Invalid captcha token."]
                        }, traceId: HttpContext.TraceIdentifier));
            }

            var result = await events.CheckInDaily(currentUser, backdated, version);
            events.PrepareCheckInResultForResponse(currentUser, result, version);

            return result;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "Check-in failed.",
                Detail = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("calendar")]
    public async Task<ActionResult<List<DailyEventResponse>>> GetEventCalendar(
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] bool includeNotableDays = false,
        [FromServices] NotableDaysService? notableDaysService = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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

        // Use user's region with fallback to "us"
        var regionCode = currentUser.Region;
        if (string.IsNullOrWhiteSpace(regionCode)) regionCode = "us";

        var calendar = await events.GetEventCalendar(
            currentUser,
            month.Value,
            year.Value,
            false,
            currentUser.Id,
            includeNotableDays ? regionCode : null);

        // If notable days were requested and we have a region code, fetch them
        if (includeNotableDays && notableDaysService != null)
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

    [HttpGet("calendar/merged")]
    public async Task<ActionResult<MergedDailyEventResponse>> GetMergedEventCalendar(
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromServices] NotableDaysService? notableDaysService = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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

        // Use user's region with fallback to "us"
        var regionCode = currentUser.Region;
        if (string.IsNullOrWhiteSpace(regionCode)) regionCode = "us";

        var calendar = await events.GetMergedEventCalendar(
            currentUser,
            month.Value,
            year.Value,
            false,
            currentUser.Id,
            regionCode,
            notableDaysService);

        return Ok(calendar);
    }

    #region Calendar Events

    [HttpGet("calendar/events")]
    [ProducesResponseType<List<SnUserCalendarEvent>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SnUserCalendarEvent>>> GetCalendarEvents(
        [FromQuery] Instant? startTime,
        [FromQuery] Instant? endTime,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var (userEvents, totalCount) = await events.GetUserCalendarEventsAsync(
            currentUser.Id,
            currentUser.Id,
            startTime,
            endTime,
            offset,
            take);

        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(userEvents);
    }

    [HttpPost("calendar/events")]
    [ProducesResponseType<SnUserCalendarEvent>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnUserCalendarEvent>> CreateCalendarEvent([FromBody] CreateCalendarEventRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var calendarEvent = await events.CreateCalendarEventAsync(currentUser.Id, request);

            if (request.IconId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
                if (file is not null)
                    calendarEvent.Icon = SnCloudFileReferenceObject.FromProtoValue(file);
            }

            if (request.BackgroundId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
                if (file is not null)
                    calendarEvent.Background = SnCloudFileReferenceObject.FromProtoValue(file);
            }

            if (request.IconId is not null || request.BackgroundId is not null)
            {
                db.Update(calendarEvent);
                await db.SaveChangesAsync();
            }

            return Created($"/api/accounts/me/calendar/events/{calendarEvent.Id}", calendarEvent);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["request"] = new[] { ex.Message }
            }, traceId: HttpContext.TraceIdentifier));
        }
    }

    [HttpGet("calendar/events/{id:guid}")]
    [ProducesResponseType<SnUserCalendarEvent>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnUserCalendarEvent>> GetCalendarEvent(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var calendarEvent = await events.GetCalendarEventAsync(id, currentUser.Id);

        if (calendarEvent == null)
            return NotFound(ApiError.NotFound("calendar event", traceId: HttpContext.TraceIdentifier));

        return Ok(calendarEvent);
    }

    [HttpPut("calendar/events/{id:guid}")]
    [ProducesResponseType<SnUserCalendarEvent>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnUserCalendarEvent>> UpdateCalendarEvent(
        Guid id,
        [FromBody] UpdateCalendarEventRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var calendarEvent = await events.UpdateCalendarEventAsync(currentUser.Id, id, request);

            if (request.IconId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
                if (file is not null)
                    calendarEvent.Icon = SnCloudFileReferenceObject.FromProtoValue(file);
            }

            if (request.BackgroundId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
                if (file is not null)
                    calendarEvent.Background = SnCloudFileReferenceObject.FromProtoValue(file);
            }

            if (request.IconId is not null || request.BackgroundId is not null)
            {
                db.Update(calendarEvent);
                await db.SaveChangesAsync();
            }

            return Ok(calendarEvent);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiError.NotFound("calendar event", traceId: HttpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["request"] = new[] { ex.Message }
            }, traceId: HttpContext.TraceIdentifier));
        }
    }

    [HttpDelete("calendar/events/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteCalendarEvent(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var deleted = await events.DeleteCalendarEventAsync(currentUser.Id, id);

        if (!deleted)
            return NotFound(ApiError.NotFound("calendar event", traceId: HttpContext.TraceIdentifier));

        return NoContent();
    }

    [HttpGet("calendar/countdown")]
    [ProducesResponseType<List<EventCountdownItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EventCountdownItem>>> GetCountdown(
        [FromQuery] int take = 5,
        [FromQuery] int offset = 0,
        [FromQuery] bool includeNotableDays = true,
        [FromQuery] string? tag = null,
        [FromServices] NotableDaysService? notableDaysService = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        // Use user's region with fallback to "us"
        var regionCode = currentUser.Region;
        if (string.IsNullOrWhiteSpace(regionCode)) regionCode = "us";

        NotableDayTag? tagFilter = null;
        if (!string.IsNullOrWhiteSpace(tag) && Enum.TryParse<NotableDayTag>(tag, true, out var parsedTag))
        {
            tagFilter = parsedTag;
        }

        var (countdownItems, totalCount) = await events.GetCountdownEventsAsync(
            currentUser,
            currentUser.Id,
            regionCode,
            notableDaysService,
            includeNotableDays,
            tagFilter,
            take,
            offset);

        Response.Headers.Append("X-Total", totalCount.ToString());
        return Ok(countdownItems);
    }

    #endregion

    #region Calendar Event Subscriptions

    [HttpGet("calendar/subscriptions")]
    [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Guid>>> GetEventSubscriptions()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var subscriptions = await events.GetCalendarEventSubscriptionsAsync(currentUser.Id);
        return Ok(subscriptions);
    }

    [HttpPost("calendar/subscriptions/{accountId:guid}")]
    [ProducesResponseType<SnCalendarEventSubscription>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SnCalendarEventSubscription>> SubscribeToEvents(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var subscription = await events.SubscribeToCalendarEventsAsync(currentUser.Id, accountId);
            events.PurgeCalendarEventSubscriptionCache(currentUser.Id);
            return Created($"/api/accounts/me/calendar/subscriptions/{accountId}", subscription);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["request"] = new[] { ex.Message }
            }, traceId: HttpContext.TraceIdentifier));
        }
    }

    [HttpDelete("calendar/subscriptions/{accountId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnsubscribeFromEvents(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var removed = await events.UnsubscribeFromCalendarEventsAsync(currentUser.Id, accountId);
        if (!removed)
            return NotFound(ApiError.NotFound("subscription", traceId: HttpContext.TraceIdentifier));

        events.PurgeCalendarEventSubscriptionCache(currentUser.Id);
        return NoContent();
    }

    [HttpGet("calendar/subscriptions/subscribers")]
    [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Guid>>> GetEventSubscribers()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var subscribers = await events.GetCalendarEventSubscribersAsync(currentUser.Id);
        return Ok(subscribers);
    }

    #endregion
}
