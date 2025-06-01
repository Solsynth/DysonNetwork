using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

[Authorize]
[ApiController]
[Route("/accounts/me")]
public class AccountCurrentController(
    AppDatabase db,
    AccountService accounts,
    FileService fs,
    FileReferenceService fileRefService,
    AccountEventService events,
    AuthService auth
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
        [MaxLength(4096)] public string? Bio { get; set; }

        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
    }

    [HttpPatch("profile")]
    public async Task<ActionResult<Profile>> UpdateProfile([FromBody] ProfileRequest request)
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

        if (request.PictureId is not null)
        {
            var picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

            var profileResourceId = $"profile:{profile.Id}";

            // Remove old references for the profile picture
            if (profile.Picture is not null) {
                var oldPictureRefs = await fileRefService.GetResourceReferencesAsync(profileResourceId, "profile.picture");
                foreach (var oldRef in oldPictureRefs)
                {
                    await fileRefService.DeleteReferenceAsync(oldRef.Id);
                }
            }

            profile.Picture = picture.ToReferenceObject();

            // Create new reference
            await fileRefService.CreateReferenceAsync(
                picture.Id, 
                "profile.picture", 
                profileResourceId
            );
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");

            var profileResourceId = $"profile:{profile.Id}";

            // Remove old references for the profile background
            if (profile.Background is not null) {
                var oldBackgroundRefs = await fileRefService.GetResourceReferencesAsync(profileResourceId, "profile.background");
                foreach (var oldRef in oldBackgroundRefs)
                {
                    await fileRefService.DeleteReferenceAsync(oldRef.Id);
                }
            }

            profile.Background = background.ToReferenceObject();

            // Create new reference
            await fileRefService.CreateReferenceAsync(
                background.Id, 
                "profile.background", 
                profileResourceId
            );
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

    [HttpGet("sessions")]
    public async Task<ActionResult<List<Auth.Session>>> GetSessions(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var query = db.AuthSessions
            .Include(session => session.Account)
            .Include(session => session.Challenge)
            .Where(session => session.Account.Id == currentUser.Id)
            .OrderByDescending(session => session.CreatedAt);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var sessions = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(sessions);
    }
}