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
            && !await accounts.CheckAuthFactorEnabled(
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
                            "Recovery code must be enabled before creating other auth factors.",
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

    public class PasskeyRegistrationStartRequest
    {
        public string DeviceId { get; set; } = null!;
        public string? DeviceName { get; set; }
        public string RpId { get; set; } = null!;
        public string RpName { get; set; } = null!;
    }

    public class PasskeyRegistrationStartResponse
    {
        public string Challenge { get; set; } = null!;
        public string RpId { get; set; } = null!;
        public string RpName { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public List<PublicKeyCredentialParameters> PubKeyCredParams { get; set; } = [];
        public int Timeout { get; set; }
        public AuthenticatorSelectionCriteria? AuthenticatorSelection { get; set; }
    }

    public class PublicKeyCredentialParameters
    {
        public string Type { get; set; } = "public-key";
        public int Alg { get; set; } = -7;
    }

    public class AuthenticatorSelectionCriteria
    {
        public string AuthenticatorAttachment { get; set; } = "platform";
        public string ResidentKey { get; set; } = "preferred";
        public string UserVerification { get; set; } = "preferred";
    }

    [HttpPost("factors/passkey/start")]
    public async Task<ActionResult<PasskeyRegistrationStartResponse>> StartPasskeyRegistration(
        [FromBody] PasskeyRegistrationStartRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        if (
            !await accounts.CheckAuthFactorEnabled(
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
                            "Recovery code must be enabled before creating passkey.",
                        ],
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );
        if (await accounts.CheckAuthFactorExists(currentUser, AccountAuthFactorType.Passkey))
            return BadRequest(
                ApiError.Validation(
                    new Dictionary<string, string[]>
                    {
                        ["factor"] = ["Passkey already exists."],
                    },
                    traceId: HttpContext.TraceIdentifier
                )
            );

        var challenge = await accounts.GeneratePasskeyChallengeAsync(currentUser, request.DeviceId);

        return Ok(new PasskeyRegistrationStartResponse
        {
            Challenge = challenge,
            RpId = request.RpId,
            RpName = request.RpName,
            UserId = currentUser.Id.ToString(),
            UserName = currentUser.Name,
            DisplayName = string.IsNullOrEmpty(currentUser.Nick) ? currentUser.Name : currentUser.Nick,
            PubKeyCredParams = [new PublicKeyCredentialParameters()],
            Timeout = 60000,
            AuthenticatorSelection = new AuthenticatorSelectionCriteria()
        });
    }

    public class PasskeyRegistrationCompleteRequest
    {
        public string DeviceId { get; set; } = null!;
        public string ClientDataJson { get; set; } = null!;
        public string AttestationObject { get; set; } = null!;
        public string? DeviceName { get; set; }
    }

    [HttpPost("factors/passkey/complete")]
    public async Task<ActionResult<SnAccountAuthFactor>> CompletePasskeyRegistration(
        [FromBody] PasskeyRegistrationCompleteRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var credential = await accounts.CompletePasskeyRegistrationAsync(
            currentUser,
            request.DeviceId,
            request.ClientDataJson,
            request.AttestationObject
        );

        if (credential == null)
            return BadRequest("Passkey registration failed.");

        var credentialJson = System.Text.Json.JsonSerializer.Serialize(credential);
        var factor = await accounts.CreateAuthFactor(currentUser, AccountAuthFactorType.Passkey, credentialJson);
        return factor is null ? BadRequest("Failed to create passkey factor.") : Ok(factor);
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
    public async Task<ActionResult<List<SnAuthClientWithSessions>>> GetDevices(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();

        Response.Headers.Append("X-Auth-Session", currentSession.Id.ToString());

        var baseQuery = db.AuthClients.Where(device => device.AccountId == currentUser.Id && device.DeletedAt == null);

        var total = await baseQuery.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var devices = await baseQuery
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var sessionDevices = devices.ConvertAll(SnAuthClientWithSessions.FromClient).ToList();
        var clientIds = sessionDevices.Select(x => x.Id).ToList();

        if (clientIds.Count > 0)
        {
            var sessionsByClientId = await db
                .AuthSessions.Where(s => clientIds.Contains(s.ClientId!.Value))
                .GroupBy(s => s.ClientId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.ToList());
            foreach (var device in sessionDevices)
                if (sessionsByClientId.TryGetValue(device.Id, out var sessions))
                    device.Sessions = sessions;
        }

        return Ok(sessionDevices);
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<List<SnAuthSession>>> GetSessions(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] SessionType? type = null,
        [FromQuery] Guid? clientId = null
    )
    {
        if (
            HttpContext.Items["CurrentUser"] is not SnAccount currentUser
            || HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession
        )
            return Unauthorized();

        var query = db
            .AuthSessions.Where(session => session.AccountId == currentUser.Id);

        if (type.HasValue)
            query = query.Where(session => session.Type == type.Value);

        if (clientId.HasValue)
            query = query.Where(session => session.ClientId == clientId.Value);

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

        try
        {
            await accounts.RequestContactVerification(currentUser, contact);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

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
