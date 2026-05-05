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
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    ApplePassService applePasses,
    RemoteAccountContactService remoteContacts,
    AccountEventService events,
    DyFileService.DyFileServiceClient files,
    Credit.SocialCreditService creditService,
    RemoteSubscriptionService remoteSubscription,
    RemoteActionLogService remoteActionLogs,
    DyAuthService.DyAuthServiceClient auth
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SnAccount>> GetCurrentIdentity()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var account = await accounts.GetAccount(userId);

        if (account != null)
        {
            if (account.Profile is null)
            {
                account.Profile = await accounts.GetOrCreateAccountProfileAsync(account.Id);
            }

            // Populate PerkSubscription from Wallet service via gRPC
            try
            {
                var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
                if (subscription is not null)
                {
                    account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
                    account.PerkLevel = account.PerkSubscription.PerkLevel;
                }
                else
                {
                    account.PerkSubscription = null;
                    account.PerkLevel = 0;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the request - PerkSubscription is optional
                Console.WriteLine($"Failed to populate PerkSubscription for account {account.Id}: {ex.Message}");
            }

            account.Contacts = await remoteContacts.ListContactsAsync(account.Id);
        }

        return Ok(account);
    }

    [HttpGet("passbook/member")]
    [Produces("application/vnd.apple.pkpass")]
    public async Task<ActionResult> GetMemberPass(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var bytes = await applePasses.GenerateMemberPassAsync(currentUser.Id, cancellationToken);
        return File(bytes, "application/vnd.apple.pkpass", "solian-member.pkpass");
    }


    public class ProfileRequest
    {
        [MaxLength(256)] public string? FirstName { get; set; }
        [MaxLength(256)] public string? MiddleName { get; set; }
        [MaxLength(256)] public string? LastName { get; set; }
        [MaxLength(1024)] public string? Gender { get; set; }
        [MaxLength(1024)] public string? Pronouns { get; set; }
        [MaxLength(1024)] public string? TimeZone { get; set; }
        [MaxLength(1024)] public string? Location { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }
        public Shared.Models.UsernameColor? UsernameColor { get; set; }
        public Instant? Birthday { get; set; }
        public List<SnProfileLink>? Links { get; set; }

        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
    }

    [HttpPatch("profile")]
    public async Task<ActionResult<SnAccountProfile>> UpdateProfile([FromBody] ProfileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var profile = await accounts.GetOrCreateAccountProfileAsync(userId);
        var changedFields = new List<string>();

        if (request.FirstName is not null) { profile.FirstName = request.FirstName; changedFields.Add("first_name"); }
        if (request.MiddleName is not null) { profile.MiddleName = request.MiddleName; changedFields.Add("middle_name"); }
        if (request.LastName is not null) { profile.LastName = request.LastName; changedFields.Add("last_name"); }
        if (request.Bio is not null) { profile.Bio = request.Bio; changedFields.Add("bio"); }
        if (request.Gender is not null) { profile.Gender = request.Gender; changedFields.Add("gender"); }
        if (request.Pronouns is not null) { profile.Pronouns = request.Pronouns; changedFields.Add("pronouns"); }
        if (request.Birthday is not null) { profile.Birthday = request.Birthday; changedFields.Add("birthday"); }
        if (request.Location is not null) { profile.Location = request.Location; changedFields.Add("location"); }
        if (request.TimeZone is not null) { profile.TimeZone = request.TimeZone; changedFields.Add("time_zone"); }
        if (request.Links is not null) { profile.Links = request.Links; changedFields.Add("links"); }
        if (request.UsernameColor is not null) { profile.UsernameColor = request.UsernameColor; changedFields.Add("username_color"); }

        if (request.PictureId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
            changedFields.Add("picture");
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
            changedFields.Add("background");
        }

        db.Update(profile);
        await db.SaveChangesAsync();
        if (changedFields.Count > 0)
        {
            remoteActionLogs.CreateActionLog(
                userId,
                ActionLogType.AccountProfileUpdate,
                new Dictionary<string, object> { ["fields"] = changedFields },
                Request.Headers.UserAgent.ToString(),
                Request.GetClientIpAddress()
            );
        }

        await accounts.PurgeAccountCache(currentUser);

        return profile;
    }

    [HttpDelete]
    public async Task<ActionResult> RequestDeleteAccount()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.RequestAccountDeletion(currentUser);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new ApiError
            {
                Code = "TOO_MANY_REQUESTS",
                Message = "You already requested account deletion within 24 hours.",
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok();
    }

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

        if (version < 2)
            result.FortuneReport = null;

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
            if (version < 2)
                result.FortuneReport = null;

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
        [FromServices] NotableDaysService? notableDaysService = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        // Use user's region with fallback to "us"
        var regionCode = currentUser.Region;
        if (string.IsNullOrWhiteSpace(regionCode)) regionCode = "us";

        var countdownItems = await events.GetUpcomingEventsAsync(
            currentUser,
            currentUser.Id,
            regionCode,
            notableDaysService,
            take);

        return Ok(countdownItems);
    }

    #endregion

    [HttpGet("actions")]
    [ProducesResponseType<List<SnActionLog>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SnActionLog>>> GetActionLogs(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var page = await remoteActionLogs.ListActionLogsPage(
            currentUser.Id,
            pageSize: Math.Max(1, take),
            pageToken: Math.Max(0, offset).ToString(),
            orderBy: "createdat desc");

        Response.Headers.Append("X-Total", page.TotalSize.ToString());

        var logs = page.ActionLogs.Select(log =>
        {
            var meta = log.Meta
                .Select(x => new KeyValuePair<string, object?>(x.Key, InfraObjectCoder.ConvertValueToObject(x.Value)))
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!);

            Guid? sessionId = null;
            if (!string.IsNullOrWhiteSpace(log.SessionId) && Guid.TryParse(log.SessionId, out var parsedSessionId))
                sessionId = parsedSessionId;

            GeoPoint? location = null;
            if (!string.IsNullOrWhiteSpace(log.Location))
            {
                try
                {
                    location = JsonSerializer.Deserialize<GeoPoint>(log.Location);
                }
                catch (JsonException)
                {
                }
            }

            return new SnActionLog
            {
                Id = Guid.TryParse(log.Id, out var parsedId) ? parsedId : Guid.NewGuid(),
                AccountId = currentUser.Id,
                Action = log.Action,
                Meta = meta,
                UserAgent = string.IsNullOrWhiteSpace(log.UserAgent) ? null : log.UserAgent,
                IpAddress = string.IsNullOrWhiteSpace(log.IpAddress) ? null : log.IpAddress,
                Location = location,
                SessionId = sessionId,
                CreatedAt = log.CreatedAt.ToInstant(),
                UpdatedAt = log.CreatedAt.ToInstant()
            };
        }).ToList();

        return Ok(logs);
    }

    [HttpGet("badges")]
    [ProducesResponseType<List<SnAccountBadge>>(StatusCodes.Status200OK)]
    [Authorize]
    public async Task<ActionResult<List<SnAccountBadge>>> GetBadges()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var badges = await db.Badges
            .Where(b => b.AccountId == currentUser.Id)
            .ToListAsync();
        return Ok(badges);
    }

    [HttpPost("badges/{id:guid}/active")]
    [Authorize]
    public async Task<ActionResult<SnAccountBadge>> ActivateBadge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.ActiveBadge(currentUser, id);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("leveling")]
    [Authorize]
    public async Task<ActionResult<SnExperienceRecord>> GetLevelingHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.ExperienceRecords
            .Where(r => r.AccountId == currentUser.Id)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var records = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(records);
    }

    [HttpGet("credits")]
    public async Task<ActionResult<bool>> GetSocialCredit()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var credit = await creditService.GetSocialCredit(currentUser.Id);
        return Ok(credit);
    }

    [HttpGet("credits/history")]
    public async Task<ActionResult<SnSocialCreditRecord>> GetCreditHistory(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var queryable = db.SocialCreditRecords
            .Where(r => r.AccountId == currentUser.Id)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var records = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(records);
    }
}
