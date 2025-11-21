using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Http;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AuthService = DysonNetwork.Pass.Auth.AuthService;
using SnAuthSession = DysonNetwork.Shared.Models.SnAuthSession;

namespace DysonNetwork.Pass.Account;

[Authorize]
[ApiController]
[Route("/api/accounts/me")]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    SubscriptionService subscriptions,
    AccountEventService events,
    AuthService auth,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    Credit.SocialCreditService creditService
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SnAccount>> GetCurrentIdentity()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var account = await db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Where(e => e.Id == userId)
            .FirstOrDefaultAsync();

        var perk = await subscriptions.GetPerkSubscriptionAsync(account!.Id);
        account.PerkSubscription = perk?.ToReference();

        return Ok(account);
    }

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

        var account = await db.Accounts.FirstAsync(a => a.Id == currentUser.Id);

        if (request.Nick is not null) account.Nick = request.Nick;
        if (request.Language is not null) account.Language = request.Language;
        if (request.Region is not null) account.Region = request.Region;

        await db.SaveChangesAsync();
        await accounts.PurgeAccountCache(currentUser);
        return currentUser;
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

        var profile = await db.AccountProfiles
            .Where(p => p.Account.Id == userId)
            .FirstOrDefaultAsync();
        if (profile is null)
            return BadRequest(new ApiError
            {
                Code = "NOT_FOUND",
                Message = "Unable to get your account.",
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });

        if (request.FirstName is not null) profile.FirstName = request.FirstName;
        if (request.MiddleName is not null) profile.MiddleName = request.MiddleName;
        if (request.LastName is not null) profile.LastName = request.LastName;
        if (request.Bio is not null) profile.Bio = request.Bio;
        if (request.Gender is not null) profile.Gender = request.Gender;
        if (request.Pronouns is not null) profile.Pronouns = request.Pronouns;
        if (request.Birthday is not null) profile.Birthday = request.Birthday;
        if (request.Location is not null) profile.Location = request.Location;
        if (request.TimeZone is not null) profile.TimeZone = request.TimeZone;
        if (request.Links is not null) profile.Links = request.Links;
        if (request.UsernameColor is not null) profile.UsernameColor = request.UsernameColor;

        if (request.PictureId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
            if (profile.Picture is not null)
                await fileRefs.DeleteResourceReferencesAsync(
                    new DeleteResourceReferencesRequest { ResourceId = profile.ResourceIdentifier }
                );
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    ResourceId = profile.ResourceIdentifier,
                    FileId = request.PictureId,
                    Usage = "profile.picture"
                }
            );
            profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
            if (profile.Background is not null)
                await fileRefs.DeleteResourceReferencesAsync(
                    new DeleteResourceReferencesRequest { ResourceId = profile.ResourceIdentifier }
                );
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    ResourceId = profile.ResourceIdentifier,
                    FileId = request.BackgroundId,
                    Usage = "profile.background"
                }
            );
            profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        db.Update(profile);
        await db.SaveChangesAsync();

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
    [RequiredPermission("global", "accounts.statuses.update")]
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
        status.IsInvisible = request.IsInvisible;
        status.IsNotDisturb = request.IsNotDisturb;
        status.IsAutomated = request.IsAutomated;
        status.Label = request.Label;
        status.AppIdentifier = request.AppIdentifier;
        status.Meta = request.Meta;
        status.ClearedAt = request.ClearedAt;

        db.Update(status);
        await db.SaveChangesAsync();
        events.PurgeStatusCache(currentUser.Id);

        return status;
    }

    [HttpPost("statuses")]
    [RequiredPermission("global", "accounts.statuses.create")]
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
                    existingStatus.IsInvisible = request.IsInvisible;
                    existingStatus.IsNotDisturb = request.IsNotDisturb;
                    existingStatus.Meta = request.Meta;
                    existingStatus.Label = request.Label;
                    db.Update(existingStatus);
                    await db.SaveChangesAsync();
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
            IsInvisible = request.IsInvisible,
            IsNotDisturb = request.IsNotDisturb,
            IsAutomated = request.IsAutomated,
            Label = request.Label,
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
    public async Task<ActionResult<SnCheckInResult>> GetCheckInResult()
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

        return result is null
            ? NotFound(ApiError.NotFound("check-in", traceId: HttpContext.TraceIdentifier))
            : Ok(result);
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<SnCheckInResult>> DoCheckIn(
        [FromBody] string? captchaToken,
        [FromQuery] Instant? backdated = null
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
            if (currentUser.PerkSubscription is null)
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
            return needsCaptcha switch
            {
                true when string.IsNullOrWhiteSpace(captchaToken) => StatusCode(423,
                    new ApiError
                    {
                        Code = "CAPTCHA_REQUIRED",
                        Message = "Captcha is required for this check-in.",
                        Status = 423,
                        TraceId = HttpContext.TraceIdentifier
                    }
                ),
                true when !await auth.ValidateCaptcha(captchaToken!) => BadRequest(ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["captchaToken"] = new[] { "Invalid captcha token." }
                    }, traceId: HttpContext.TraceIdentifier)),
                _ => await events.CheckInDaily(currentUser, backdated)
            };
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
    public async Task<ActionResult<List<DailyEventResponse>>> GetEventCalendar([FromQuery] int? month,
        [FromQuery] int? year)
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

        var calendar = await events.GetEventCalendar(currentUser, month.Value, year.Value);
        return Ok(calendar);
    }

    [HttpGet("actions")]
    [ProducesResponseType<List<ActionLog>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ActionLog>>> GetActionLogs(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var query = db.ActionLogs
            .Where(log => log.AccountId == currentUser.Id)
            .OrderByDescending(log => log.CreatedAt);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var logs = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(logs);
    }

    [HttpGet("factors")]
    public async Task<ActionResult<List<SnAccountAuthFactor>>> GetAuthFactors()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var factors = await db.AccountAuthFactors
            .Include(f => f.Account)
            .Where(f => f.Account.Id == currentUser.Id)
            .ToListAsync();

        return Ok(factors);
    }

    public class AuthFactorRequest
    {
        public Shared.Models.AccountAuthFactorType Type { get; set; }
        public string? Secret { get; set; }
    }

    [HttpPost("factors")]
    [Authorize]
    public async Task<ActionResult<SnAccountAuthFactor>> CreateAuthFactor([FromBody] AuthFactorRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (await accounts.CheckAuthFactorExists(currentUser, request.Type))
            return BadRequest(new ApiError
            {
                Code = "ALREADY_EXISTS",
                Message = $"Auth factor with type {request.Type} already exists.",
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });

        var factor = await accounts.CreateAuthFactor(currentUser, request.Type, request.Secret);
        return Ok(factor);
    }

    [HttpPost("factors/{id:guid}/enable")]
    [Authorize]
    public async Task<ActionResult<SnAccountAuthFactor>> EnableAuthFactor(Guid id, [FromBody] string? code)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var factor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null) return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));

        try
        {
            factor = await accounts.EnableAuthFactor(factor, code);
            return Ok(factor);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "Failed to enable auth factor.",
                Detail = ex.Message,
                Status = 400,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("factors/{id:guid}/disable")]
    [Authorize]
    public async Task<ActionResult<SnAccountAuthFactor>> DisableAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var factor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null) return NotFound();

        try
        {
            factor = await accounts.DisableAuthFactor(factor);
            return Ok(factor);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("factors/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountAuthFactor>> DeleteAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var factor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null) return NotFound();

        try
        {
            await accounts.DeleteAuthFactor(factor);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("devices")]
    [Authorize]
    public async Task<ActionResult<List<SnAuthClientWithChallenge>>> GetDevices()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        var devices = await db.AuthClients
            .Where(device => device.AccountId == currentUser.Id)
            .ToListAsync();

        var challengeDevices = devices.Select(SnAuthClientWithChallenge.FromClient).ToList();
        var deviceIds = challengeDevices.Select(x => x.Id).ToList();

        var authChallenges = await db.AuthChallenges
            .Where(c => c.ClientId != null && deviceIds.Contains(c.ClientId.Value))
            .GroupBy(c => c.ClientId)
            .ToDictionaryAsync(c => c.Key!.Value, c => c.ToList());
        foreach (var challengeDevice in challengeDevices)
            if (authChallenges.TryGetValue(challengeDevice.Id, out var challenge))
                challengeDevice.Challenges = challenge;

        return Ok(challengeDevices);
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<List<SnAuthSession>>> GetSessions(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        var query = db.AuthSessions
            .Include(session => session.Account)
            .Include(session => session.Challenge)
            .Where(session => session.Account.Id == currentUser.Id);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());
        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        var sessions = await query
            .OrderByDescending(x => x.LastGrantedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAuthSession>> DeleteSession(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.DeleteSession(currentUser, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("devices/{deviceId}")]
    [Authorize]
    public async Task<ActionResult<SnAuthSession>> DeleteDevice(string deviceId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.DeleteDevice(currentUser, deviceId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("sessions/current")]
    [Authorize]
    public async Task<ActionResult<SnAuthSession>> DeleteCurrentSession()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        try
        {
            await accounts.DeleteSession(currentUser, currentSession.Id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("devices/{deviceId}/label")]
    [Authorize]
    public async Task<ActionResult<SnAuthSession>> UpdateDeviceLabel(string deviceId, [FromBody] string label)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            await accounts.UpdateDeviceName(currentUser, deviceId, label);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("devices/current/label")]
    [Authorize]
    public async Task<ActionResult<SnAuthSession>> UpdateCurrentDeviceLabel([FromBody] string label)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        var device = await db.AuthClients.FirstOrDefaultAsync(d => d.Id == currentSession.Challenge.ClientId);
        if (device is null) return NotFound();

        try
        {
            await accounts.UpdateDeviceName(currentUser, device.DeviceId, label);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("contacts")]
    [Authorize]
    public async Task<ActionResult<List<SnAccountContact>>> GetContacts()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contacts = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id)
            .ToListAsync();

        return Ok(contacts);
    }

    public class AccountContactRequest
    {
        [Required] public Shared.Models.AccountContactType Type { get; set; }
        [Required] public string Content { get; set; } = null!;
    }

    [HttpPost("contacts")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> CreateContact([FromBody] AccountContactRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var contact = await accounts.CreateContactMethod(currentUser, request.Type, request.Content);
            return Ok(contact);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("contacts/{id:guid}/verify")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> VerifyContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contact = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null) return NotFound();

        try
        {
            await accounts.VerifyContactMethod(currentUser, contact);
            return Ok(contact);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("contacts/{id:guid}/primary")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> SetPrimaryContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contact = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null) return NotFound();

        try
        {
            contact = await accounts.SetContactMethodPrimary(currentUser, contact);
            return Ok(contact);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("contacts/{id:guid}/public")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> SetPublicContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contact = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null) return NotFound();

        try
        {
            contact = await accounts.SetContactMethodPublic(currentUser, contact, true);
            return Ok(contact);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("contacts/{id:guid}/public")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> UnsetPublicContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contact = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null) return NotFound();

        try
        {
            contact = await accounts.SetContactMethodPublic(currentUser, contact, false);
            return Ok(contact);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("contacts/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountContact>> DeleteContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var contact = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null) return NotFound();

        try
        {
            await accounts.DeleteContactMethod(currentUser, contact);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
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
    public async Task<ActionResult<SocialCreditRecord>> GetCreditHistory(
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
