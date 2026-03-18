using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Account;

[Authorize]
[RequireInteractiveSession]
[ApiController]
[Route("/api")]
public class AccountSecurityController(
    AppDatabase db,
    AccountService accounts,
    Auth.AuthService auth
) : ControllerBase
{
    public record AuthorizedAppResponse(
        Guid Id,
        Guid AppId,
        AuthorizedAppType Type,
        string? AppSlug,
        string? AppName,
        NodaTime.Instant LastAuthorizedAt,
        NodaTime.Instant? LastUsedAt
    );

    [HttpGet("identity")]
    public async Task<ActionResult<SnAccount>> GetCurrentIdentity()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var account = await db.Accounts.Where(e => e.Id == currentUser.Id).FirstOrDefaultAsync();
        return Ok(account);
    }

    [HttpGet("factors")]
    public async Task<ActionResult<List<SnAccountAuthFactor>>> GetAuthFactors()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var factors = await db
            .AccountAuthFactors.Where(f => f.AccountId == currentUser.Id)
            .ToListAsync();

        return Ok(factors);
    }

    public class AuthFactorRequest
    {
        public AccountAuthFactorType Type { get; set; }
        public string? Secret { get; set; }
    }

    [HttpPost("factors")]
    public async Task<ActionResult<SnAccountAuthFactor>> CreateAuthFactor(
        [FromBody] AuthFactorRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        if (
            request.Type != AccountAuthFactorType.RecoveryCode
            && !await accounts.CheckAuthFactorExists(
                currentUser,
                AccountAuthFactorType.RecoveryCode
            )
        )
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["factor"] =
                        [
                            "Recovery code must be created before creating other auth factors.",
                        ],
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );
        if (await accounts.CheckAuthFactorExists(currentUser, request.Type))
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["factor"] = [$"Auth factor with type {request.Type} already exists."],
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );

        var factor = await accounts.CreateAuthFactor(currentUser, request.Type, request.Secret);
        return factor is null ? BadRequest("Invalid factor request.") : Ok(factor);
    }

    [HttpPost("factors/{id:guid}/enable")]
    public async Task<ActionResult<SnAccountAuthFactor>> EnableAuthFactor(
        Guid id,
        [FromBody] string? code
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var factor = await db
            .AccountAuthFactors.Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null)
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));

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
    public async Task<ActionResult<SnAccountAuthFactor>> DisableAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var factor = await db
            .AccountAuthFactors.Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null)
            return NotFound();

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
    public async Task<ActionResult> DeleteAuthFactor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var factor = await db
            .AccountAuthFactors.Where(f => f.AccountId == currentUser.Id && f.Id == id)
            .FirstOrDefaultAsync();
        if (factor is null)
            return NotFound();

        await accounts.DeleteAuthFactor(factor);
        return NoContent();
    }

    [HttpGet("devices")]
    public async Task<ActionResult<List<SnAuthClientWithSessions>>> GetDevices()
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();

        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        var devices = await db
            .AuthClients.Where(device => device.AccountId == currentUser.Id)
            .ToListAsync();

        var sessionDevices = devices.ConvertAll(SnAuthClientWithSessions.FromClient).ToList();
        var clientIds = sessionDevices.Select(x => x.Id).ToList();

        var authSessions = await db
            .AuthSessions.Where(c => c.ClientId != null && clientIds.Contains(c.ClientId.Value))
            .GroupBy(c => c.ClientId!.Value)
            .ToDictionaryAsync(c => c.Key, c => c.ToList());
        foreach (var dev in sessionDevices)
            if (authSessions.TryGetValue(dev.Id, out var challenge))
                dev.Sessions = challenge;

        return Ok(sessionDevices);
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<List<SnAuthSession>>> GetSessions(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();

        var query = db
            .AuthSessions.OrderByDescending(x => x.LastGrantedAt)
            .Where(session => session.AccountId == currentUser.Id);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());
        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        var sessions = await query.Skip(offset).Take(take).ToListAsync();
        return Ok(sessions);
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<ActionResult> DeleteSession(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        await accounts.DeleteSession(currentUser, id);
        return NoContent();
    }

    [HttpDelete("devices/{deviceId}")]
    public async Task<ActionResult> DeleteDevice(string deviceId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        await accounts.DeleteDevice(currentUser, deviceId);
        return NoContent();
    }

    [HttpDelete("sessions/current")]
    public async Task<ActionResult> DeleteCurrentSession()
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();
        await accounts.DeleteSession(currentUser, currentSession.Id);
        return NoContent();
    }

    [HttpGet("authorized-apps")]
    public async Task<ActionResult<List<AuthorizedAppResponse>>> GetAuthorizedApps(
        [FromQuery] AuthorizedAppType? type
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var query = db
            .AuthorizedApps.Where(x => x.AccountId == currentUser.Id)
            .Where(x => x.DeletedAt == null);
        if (type.HasValue)
            query = query.Where(x => x.Type == type.Value);

        var apps = await query
            .OrderByDescending(x => x.LastUsedAt ?? x.LastAuthorizedAt)
            .ToListAsync();

        return Ok(
            apps.Select(x => new AuthorizedAppResponse(
                x.Id,
                x.AppId,
                x.Type,
                x.AppSlug,
                x.AppName,
                x.LastAuthorizedAt,
                x.LastUsedAt
            ))
        );
    }

    [HttpDelete("authorized-apps/{appId:guid}")]
    public async Task<ActionResult> DeauthorizeApp(
        [FromRoute] Guid appId,
        [FromQuery] AuthorizedAppType? type
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var count = await auth.RevokeAuthorizedAppAccessAsync(currentUser.Id, appId, type);
        if (count == 0)
            return NotFound("Authorized app was not found.");

        return NoContent();
    }

    [HttpPatch("devices/{deviceId}/label")]
    public async Task<ActionResult> UpdateDeviceLabel(string deviceId, [FromBody] string label)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        await accounts.UpdateDeviceName(currentUser, deviceId, label);
        return NoContent();
    }

    [HttpPatch("devices/current/label")]
    public async Task<ActionResult> UpdateCurrentDeviceLabel([FromBody] string label)
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();

        var device = await db.AuthClients.FirstOrDefaultAsync(d => d.Id == currentSession.ClientId);
        if (device is null)
            return NotFound();

        await accounts.UpdateDeviceName(currentUser, device.DeviceId, label);
        return NoContent();
    }

    [HttpGet("contacts")]
    public async Task<ActionResult<List<SnAccountContact>>> GetContacts()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contacts = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id)
            .ToListAsync();
        return Ok(contacts);
    }

    public class AccountContactRequest
    {
        public AccountContactType Type { get; set; }
        public string Content { get; set; } = null!;
    }

    [HttpPost("contacts")]
    public async Task<ActionResult<SnAccountContact>> CreateContact(
        [FromBody] AccountContactRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await accounts.CreateContactMethod(
            currentUser,
            request.Type,
            request.Content
        );
        return Ok(contact);
    }

    [HttpPost("contacts/{id:guid}/verify")]
    public async Task<ActionResult<SnAccountContact>> VerifyContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null)
            return NotFound();

        await accounts.VerifyContactMethod(currentUser, contact);
        return Ok(contact);
    }

    [HttpPost("contacts/{id:guid}/primary")]
    public async Task<ActionResult<SnAccountContact>> SetPrimaryContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null)
            return NotFound();
        contact = await accounts.SetContactMethodPrimary(currentUser, contact);
        return Ok(contact);
    }

    [HttpPost("contacts/{id:guid}/public")]
    public async Task<ActionResult<SnAccountContact>> SetPublicContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null)
            return NotFound();
        contact = await accounts.SetContactMethodPublic(currentUser, contact, true);
        return Ok(contact);
    }

    [HttpDelete("contacts/{id:guid}/public")]
    public async Task<ActionResult<SnAccountContact>> UnsetPublicContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null)
            return NotFound();
        contact = await accounts.SetContactMethodPublic(currentUser, contact, false);
        return Ok(contact);
    }

    [HttpDelete("contacts/{id:guid}")]
    public async Task<ActionResult> DeleteContact(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        var contact = await db
            .AccountContacts.Where(c => c.AccountId == currentUser.Id && c.Id == id)
            .FirstOrDefaultAsync();
        if (contact is null)
            return NotFound();
        await accounts.DeleteContactMethod(currentUser, contact);
        return NoContent();
    }
}
