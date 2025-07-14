using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AuthService = DysonNetwork.Pass.Auth.AuthService;
using AuthSession = DysonNetwork.Pass.Auth.AuthSession;
using ChallengePlatform = DysonNetwork.Pass.Auth.ChallengePlatform;

namespace DysonNetwork.Pass.Account;

[Authorize]
[ApiController]
[Route("/api/accounts/me")]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    AccountEventService events,
    AuthService auth,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Account>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Account>> GetCurrentIdentity()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var account = await db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Where(e => e.Id == userId)
            .FirstOrDefaultAsync();

        return Ok(account);
    }

    public class BasicInfoRequest
    {
        [MaxLength(256)] public string? Nick { get; set; }
        [MaxLength(32)] public string? Language { get; set; }
    }

    [HttpPatch]
    public async Task<ActionResult<Account>> UpdateBasicInfo([FromBody] BasicInfoRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var account = await db.Accounts.FirstAsync(a => a.Id == currentUser.Id);

        if (request.Nick is not null) account.Nick = request.Nick;
        if (request.Language is not null) account.Language = request.Language;

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
        public Instant? Birthday { get; set; }

        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
    }

    [HttpPatch("profile")]
    public async Task<ActionResult<AccountProfile>> UpdateProfile([FromBody] ProfileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var profile = await db.AccountProfiles
            .Where(p => p.Account.Id == userId)
            .FirstOrDefaultAsync();
        if (profile is null) return BadRequest("Unable to get your account.");

        if (request.FirstName is not null) profile.FirstName = request.FirstName;
        if (request.MiddleName is not null) profile.MiddleName = request.MiddleName;
        if (request.LastName is not null) profile.LastName = request.LastName;
        if (request.Bio is not null) profile.Bio = request.Bio;
        if (request.Gender is not null) profile.Gender = request.Gender;
        if (request.Pronouns is not null) profile.Pronouns = request.Pronouns;
        if (request.Birthday is not null) profile.Birthday = request.Birthday;
        if (request.Location is not null) profile.Location = request.Location;
        if (request.TimeZone is not null) profile.TimeZone = request.TimeZone;

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
            profile.Picture = CloudFileReferenceObject.FromProtoValue(file);
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
            profile.Background = CloudFileReferenceObject.FromProtoValue(file);
        }

        db.Update(profile);
        await db.SaveChangesAsync();

        await accounts.PurgeAccountCache(currentUser);

        return profile;
    }

    [HttpDelete]
    public async Task<ActionResult> RequestDeleteAccount()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            await accounts.RequestAccountDeletion(currentUser);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("You already requested account deletion within 24 hours.");
        }

        return Ok();
    }

    [HttpGet("statuses")]
    public async Task<ActionResult<Status>> GetCurrentStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var status = await events.GetStatus(currentUser.Id);
        return Ok(status);
    }

    [HttpPatch("statuses")]
    [RequiredPermission("global", "accounts.statuses.update")]
    public async Task<ActionResult<Status>> UpdateStatus([FromBody] AccountController.StatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var now = SystemClock.Instance.GetCurrentInstant();
        var status = await db.AccountStatuses
            .Where(e => e.AccountId == currentUser.Id)
            .Where(e => e.ClearedAt == null || e.ClearedAt > now)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        if (status is null) return NotFound();

        status.Attitude = request.Attitude;
        status.IsInvisible = request.IsInvisible;
        status.IsNotDisturb = request.IsNotDisturb;
        status.Label = request.Label;
        status.ClearedAt = request.ClearedAt;

        db.Update(status);
        await db.SaveChangesAsync();
        events.PurgeStatusCache(currentUser.Id);

        return status;
    }

    [HttpPost("statuses")]
    [RequiredPermission("global", "accounts.statuses.create")]
    public async Task<ActionResult<Status>> CreateStatus([FromBody] AccountController.StatusRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var status = new Status
        {
            AccountId = currentUser.Id,
            Attitude = request.Attitude,
            IsInvisible = request.IsInvisible,
            IsNotDisturb = request.IsNotDisturb,
            Label = request.Label,
            ClearedAt = request.ClearedAt
        };

        return await events.CreateStatus(currentUser, status);
    }

    [HttpDelete("me/statuses")]
    public async Task<ActionResult> DeleteStatus()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var now = SystemClock.Instance.GetCurrentInstant();
        var status = await db.AccountStatuses
            .Where(s => s.AccountId == currentUser.Id)
            .Where(s => s.ClearedAt == null || s.ClearedAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        if (status is null) return NotFound();

        await events.ClearStatus(currentUser, status);
        return NoContent();
    }

    [HttpGet("check-in")]
    public async Task<ActionResult<CheckInResult>> GetCheckInResult()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
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

        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<CheckInResult>> DoCheckIn([FromBody] string? captchaToken)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var isAvailable = await events.CheckInDailyIsAvailable(currentUser);
        if (!isAvailable)
            return BadRequest("Check-in is not available for today.");

        try
        {
            var needsCaptcha = await events.CheckInDailyDoAskCaptcha(currentUser);
            return needsCaptcha switch
            {
                true when string.IsNullOrWhiteSpace(captchaToken) => StatusCode(423,
                    "Captcha is required for this check-in."),
                true when !await auth.ValidateCaptcha(captchaToken!) => BadRequest("Invalid captcha token."),
                _ => await events.CheckInDaily(currentUser)
            };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("calendar")]
    public async Task<ActionResult<List<DailyEventResponse>>> GetEventCalendar([FromQuery] int? month,
        [FromQuery] int? year)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var currentDate = SystemClock.Instance.GetCurrentInstant().InUtc().Date;
        month ??= currentDate.Month;
        year ??= currentDate.Year;

        if (month is < 1 or > 12) return BadRequest("Invalid month.");
        if (year < 1) return BadRequest("Invalid year.");

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<List<AccountAuthFactor>>> GetAuthFactors()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var factors = await db.AccountAuthFactors
            .Include(f => f.Account)
            .Where(f => f.Account.Id == currentUser.Id)
            .ToListAsync();

        return Ok(factors);
    }

    public class AuthFactorRequest
    {
        public AccountAuthFactorType Type { get; set; }
        public string? Secret { get; set; }
    }

    [HttpPost("factors")]
    [Authorize]
    public async Task<ActionResult<AccountAuthFactor>> CreateAuthFactor([FromBody] AuthFactorRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        if (await accounts.CheckAuthFactorExists(currentUser, request.Type))
            return BadRequest($"Auth factor with type {request.Type} is already exists.");

        var factor = await accounts.CreateAuthFactor(currentUser, request.Type, request.Secret);
        return Ok(factor);
    }

    [HttpPost("factors/{id:guid}/enable")]
    [Authorize]
    public async Task<ActionResult<AccountAuthFactor>> EnableAuthFactor(Guid id, [FromBody] string? code)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var factor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null) return NotFound();

        try
        {
            factor = await accounts.EnableAuthFactor(factor, code);
            return Ok(factor);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("factors/{id:guid}/disable")]
    [Authorize]
    public async Task<ActionResult<AccountAuthFactor>> DisableAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<AccountAuthFactor>> DeleteAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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

    public class AuthorizedDevice
    {
        public string? Label { get; set; }
        public string UserAgent { get; set; } = null!;
        public string DeviceId { get; set; } = null!;
        public ChallengePlatform Platform { get; set; }
        public List<AuthSession> Sessions { get; set; } = [];
    }

    [HttpGet("devices")]
    [Authorize]
    public async Task<ActionResult<List<AuthorizedDevice>>> GetDevices()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser ||
            HttpContext.Items["CurrentSession"] is not AuthSession currentSession) return Unauthorized();

        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        // Group sessions by the related DeviceId, then create an AuthorizedDevice for each group.
        var deviceGroups = await db.AuthSessions
            .Where(s => s.Account.Id == currentUser.Id)
            .Include(s => s.Challenge)
            .GroupBy(s => s.Challenge.DeviceId!)
            .Select(g => new AuthorizedDevice
            {
                DeviceId = g.Key!,
                UserAgent = g.First(x => x.Challenge.UserAgent != null).Challenge.UserAgent!,
                Platform = g.First().Challenge.Platform!,
                Label = g.Where(x => !string.IsNullOrWhiteSpace(x.Label)).Select(x => x.Label).FirstOrDefault(),
                Sessions = g
                    .OrderByDescending(x => x.LastGrantedAt)
                    .ToList()
            })
            .ToListAsync();
        deviceGroups = deviceGroups
            .OrderByDescending(s => s.Sessions.First().LastGrantedAt)
            .ToList();

        return Ok(deviceGroups);
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<ActionResult<List<AuthSession>>> GetSessions(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser ||
            HttpContext.Items["CurrentSession"] is not AuthSession currentSession) return Unauthorized();

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
    public async Task<ActionResult<AuthSession>> DeleteSession(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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

    [HttpDelete("sessions/current")]
    [Authorize]
    public async Task<ActionResult<AuthSession>> DeleteCurrentSession()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser ||
            HttpContext.Items["CurrentSession"] is not AuthSession currentSession) return Unauthorized();

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

    [HttpPatch("sessions/{id:guid}/label")]
    public async Task<ActionResult<AuthSession>> UpdateSessionLabel(Guid id, [FromBody] string label)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            await accounts.UpdateSessionLabel(currentUser, id, label);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("sessions/current/label")]
    public async Task<ActionResult<AuthSession>> UpdateCurrentSessionLabel([FromBody] string label)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser ||
            HttpContext.Items["CurrentSession"] is not AuthSession currentSession) return Unauthorized();

        try
        {
            await accounts.UpdateSessionLabel(currentUser, currentSession.Id, label);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("contacts")]
    [Authorize]
    public async Task<ActionResult<List<AccountContact>>> GetContacts()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var contacts = await db.AccountContacts
            .Where(c => c.AccountId == currentUser.Id)
            .ToListAsync();

        return Ok(contacts);
    }

    public class AccountContactRequest
    {
        [Required] public AccountContactType Type { get; set; }
        [Required] public string Content { get; set; } = null!;
    }

    [HttpPost("contacts")]
    [Authorize]
    public async Task<ActionResult<AccountContact>> CreateContact([FromBody] AccountContactRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<AccountContact>> VerifyContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<AccountContact>> SetPrimaryContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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

    [HttpDelete("contacts/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<AccountContact>> DeleteContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
    [ProducesResponseType<List<AccountBadge>>(StatusCodes.Status200OK)]
    [Authorize]
    public async Task<ActionResult<List<AccountBadge>>> GetBadges()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var badges = await db.Badges
            .Where(b => b.AccountId == currentUser.Id)
            .ToListAsync();
        return Ok(badges);
    }

    [HttpPost("badges/{id:guid}/active")]
    [Authorize]
    public async Task<ActionResult<AccountBadge>> ActivateBadge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

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
}