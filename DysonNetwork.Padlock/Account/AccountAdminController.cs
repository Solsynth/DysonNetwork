using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using DysonNetwork.Padlock.Models;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    AppDatabase db,
    AccountService accounts,
    RemoteRingService ring,
    ILocalizationService localizer,
    DyProfileService.DyProfileServiceClient profiles,
    DySocialCreditService.DySocialCreditServiceClient socialCredits,
    DyPublisherService.DyPublisherServiceClient publishers,
    DyPublisherRatingService.DyPublisherRatingServiceClient publisherRatings
) : ControllerBase
{
    public class AccountAuthFactorSummary
    {
        public Guid Id { get; set; }
        public AccountAuthFactorType Type { get; set; }
        public int Trustworthy { get; set; }
        public bool HasSecret { get; set; }
        public Dictionary<string, object>? Config { get; set; }
        public Instant? EnabledAt { get; set; }
        public Instant? ExpiredAt { get; set; }
        public Instant CreatedAt { get; set; }
        public Instant UpdatedAt { get; set; }
    }

    public class AdminAccountSummaryResponse
    {
        public SnAccount Account { get; set; } = null!;
        public string? PrimaryEmail { get; set; }
        public int ContactCount { get; set; }
        public int AuthFactorCount { get; set; }
        public bool HasPassword { get; set; }
        public int ActiveSessionCount { get; set; }
        public int ActiveDeviceCount { get; set; }
        public SnAccountPunishment? ActivePunishment { get; set; }
    }

    public class AdminAccountDetailResponse
    {
        public SnAccount Account { get; set; } = null!;
        public List<SnAccountContact> Contacts { get; set; } = [];
        public List<AccountAuthFactorSummary> AuthFactors { get; set; } = [];
        public int ActiveSessionCount { get; set; }
        public int ActiveDeviceCount { get; set; }
        public SnAccountPunishment? ActivePunishment { get; set; }
        public List<SnAccountPunishment> ActivePunishments { get; set; } = [];
    }

    public class SuspendAccountRequest
    {
        [MaxLength(8192)] public string Reason { get; set; } = string.Empty;
        public Instant? ExpiredAt { get; set; }
        public PunishmentType Type { get; set; } = PunishmentType.DisableAccount;
        public bool RevokeSessions { get; set; } = true;
        public double? SocialCreditReduction { get; set; }
        public double? PublisherRatingReduction { get; set; }
        public List<string>? PublisherNames { get; set; }
    }

    public class SendAdminNotificationRequest
    {
        public Guid? AccountId { get; set; }
        public List<Guid>? AccountIds { get; set; }
        public bool BroadcastToAll { get; set; }
        [MaxLength(1024)] public string Topic { get; set; } = string.Empty;
        [MaxLength(1024)] public string? Title { get; set; }
        [MaxLength(1024)] public string? Subtitle { get; set; }
        [MaxLength(8192)] public string? Body { get; set; }
        [MaxLength(4096)] public string? ActionUri { get; set; }
        [MaxLength(256)] public string? PushType { get; set; }
        public bool IsSilent { get; set; }
        public bool IsSavable { get; set; } = true;
        public Dictionary<string, object?>? Meta { get; set; }
    }

    public class SendAdminEmailRequest
    {
        public Guid? AccountId { get; set; }
        public List<Guid>? AccountIds { get; set; }
        public bool BroadcastToAll { get; set; }
        [MaxLength(1024)] public string Subject { get; set; } = string.Empty;
        [MaxLength(1_000_000)] public string HtmlBody { get; set; } = string.Empty;
    }

    public class ExportAdminEmailContactsRequest
    {
        public Guid? AccountId { get; set; }
        public List<Guid>? AccountIds { get; set; }
        public bool BroadcastToAll { get; set; }
    }

    public class AdminAccountContactRequest
    {
        public AccountContactType Type { get; set; }
        [MaxLength(1024)] public string Content { get; set; } = string.Empty;
    }

    public class UpdateAdminAccountContactRequest
    {
        public AccountContactType? Type { get; set; }
        [MaxLength(1024)] public string? Content { get; set; }
    }

    public class SetAdminContactVisibilityRequest
    {
        public bool IsPublic { get; set; }
    }

    public class AdminContactVerificationRequest
    {
        public Instant? VerifiedAt { get; set; }
    }

    public class AdminAccountAuthFactorRequest
    {
        public AccountAuthFactorType Type { get; set; }
        public string? Secret { get; set; }
        public bool Enable { get; set; } = true;
        public string? Code { get; set; }
    }

    public class AdminResetPasswordFactorRequest
    {
        [MaxLength(4096)] public string NewPassword { get; set; } = string.Empty;
        public bool RevokeSessions { get; set; } = true;
    }

    public class UpdateAdminDeviceLabelRequest
    {
        [MaxLength(1024)] public string Label { get; set; } = string.Empty;
    }

    public class AdminMessageDispatchResponse
    {
        public int Requested { get; set; }
        public int Resolved { get; set; }
        public int Sent { get; set; }
        public int Skipped { get; set; }
        public bool BroadcastToAll { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<AdminAccountSummaryResponse>>> ListAccounts(
        [FromQuery] string? query = null,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? orderBy = null
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var accountsQuery = db.Accounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            accountsQuery = accountsQuery.Where(a =>
                EF.Functions.ILike(a.Name, $"%{probe}%") || EF.Functions.ILike(a.Nick, $"%{probe}%"));
        }

        accountsQuery = orderBy switch
        {
            "name" => accountsQuery.OrderBy(a => a.Name),
            "name_desc" => accountsQuery.OrderByDescending(a => a.Name),
            "created_at_desc" => accountsQuery.OrderByDescending(a => a.CreatedAt),
            _ => accountsQuery.OrderBy(a => a.Id)
        };

        var total = await accountsQuery.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var localAccounts = await accountsQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        if (localAccounts.Count == 0)
            return Ok(new List<AdminAccountSummaryResponse>());

        var hydratedAccounts = await HydrateAccountsAsync(localAccounts);
        var accountIds = hydratedAccounts.Select(a => a.Id).ToList();
        var now = SystemClock.Instance.GetCurrentInstant();

        var primaryEmails = await db.AccountContacts
            .AsNoTracking()
            .Where(c => accountIds.Contains(c.AccountId) && c.Type == AccountContactType.Email)
            .OrderByDescending(c => c.IsPrimary)
            .ThenByDescending(c => c.VerifiedAt)
            .GroupBy(c => c.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                PrimaryEmail = g.Select(c => c.Content).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.AccountId, x => x.PrimaryEmail);

        var contactCounts = await db.AccountContacts
            .AsNoTracking()
            .Where(c => accountIds.Contains(c.AccountId))
            .GroupBy(c => c.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        var factorGroups = await db.AccountAuthFactors
            .AsNoTracking()
            .Where(f => accountIds.Contains(f.AccountId))
            .GroupBy(f => f.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Count = g.Count(),
                HasPassword = g.Any(f => f.Type == AccountAuthFactorType.Password && f.EnabledAt != null)
            })
            .ToDictionaryAsync(x => x.AccountId, x => new { x.Count, x.HasPassword });

        var sessionCounts = await db.AuthSessions
            .AsNoTracking()
            .Where(s => accountIds.Contains(s.AccountId) && (s.ExpiredAt == null || s.ExpiredAt > now))
            .GroupBy(s => s.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        var deviceCounts = await db.AuthClients
            .AsNoTracking()
            .Where(c => accountIds.Contains(c.AccountId) && c.DeletedAt == null)
            .GroupBy(c => c.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        var activePunishments = await db.Punishments
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AccountId) && (p.ExpiredAt == null || p.ExpiredAt > now))
            .ToListAsync();
        await accounts.HydratePunishmentAccountBatch(activePunishments);
        var punishmentLookup = activePunishments
            .GroupBy(p => p.AccountId)
            .ToDictionary(g => g.Key, SelectMostSeverePunishment);

        var response = hydratedAccounts.Select(account =>
        {
            factorGroups.TryGetValue(account.Id, out var factors);
            return new AdminAccountSummaryResponse
            {
                Account = account,
                PrimaryEmail = primaryEmails.GetValueOrDefault(account.Id),
                ContactCount = contactCounts.GetValueOrDefault(account.Id),
                AuthFactorCount = factors?.Count ?? 0,
                HasPassword = factors?.HasPassword ?? false,
                ActiveSessionCount = sessionCounts.GetValueOrDefault(account.Id),
                ActiveDeviceCount = deviceCounts.GetValueOrDefault(account.Id),
                ActivePunishment = punishmentLookup.GetValueOrDefault(account.Id)
            };
        }).ToList();

        return Ok(response);
    }

    [HttpGet("{name}")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<AdminAccountDetailResponse>> GetAccount(string name)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var hydratedAccount = (await HydrateAccountsAsync([account])).First();
        var localAccount = await db.Accounts
            .AsNoTracking()
            .Include(a => a.Contacts)
            .Include(a => a.AuthFactors)
            .FirstOrDefaultAsync(a => a.Id == account.Id);
        if (localAccount is null)
            return NotFound();

        hydratedAccount.Contacts = localAccount.Contacts;
        hydratedAccount.AuthFactors = localAccount.AuthFactors;

        var now = SystemClock.Instance.GetCurrentInstant();
        var activePunishments = await db.Punishments
            .AsNoTracking()
            .Where(p => p.AccountId == account.Id && (p.ExpiredAt == null || p.ExpiredAt > now))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        await accounts.HydratePunishmentAccountBatch(activePunishments);

        var activeSessionCount = await db.AuthSessions
            .AsNoTracking()
            .CountAsync(s => s.AccountId == account.Id && (s.ExpiredAt == null || s.ExpiredAt > now));

        var activeDeviceCount = await db.AuthClients
            .AsNoTracking()
            .CountAsync(c => c.AccountId == account.Id && c.DeletedAt == null);

        return Ok(new AdminAccountDetailResponse
        {
            Account = hydratedAccount,
            Contacts = localAccount.Contacts,
            AuthFactors = localAccount.AuthFactors.Select(ToAuthFactorSummary).ToList(),
            ActiveSessionCount = activeSessionCount,
            ActiveDeviceCount = activeDeviceCount,
            ActivePunishment = SelectMostSeverePunishment(activePunishments),
            ActivePunishments = activePunishments
        });
    }

    [HttpGet("{name}/devices")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAuthClientWithSessions>>> ListAccountDevices(
        string name,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool includeSessions = true
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var query = db.AuthClients
            .AsNoTracking()
            .Where(device => device.AccountId == account.Id);
        if (!includeDeleted)
            query = query.Where(device => device.DeletedAt == null);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var devices = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var response = devices.Select(SnAuthClientWithSessions.FromClient).ToList();
        if (!includeSessions || response.Count == 0)
            return Ok(response);

        var clientIds = response.Select(x => x.Id).ToList();
        var sessionsByClientId = await db.AuthSessions
            .AsNoTracking()
            .Where(s => s.ClientId.HasValue && clientIds.Contains(s.ClientId.Value))
            .OrderByDescending(s => s.LastGrantedAt)
            .GroupBy(s => s.ClientId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        foreach (var device in response)
            if (sessionsByClientId.TryGetValue(device.Id, out var sessions))
                device.Sessions = sessions;

        return Ok(response);
    }

    [HttpPatch("{name}/devices/{deviceId}/label")]
    [AskPermission(PermissionKeys.AccountDevicesManage)]
    public async Task<IActionResult> UpdateAccountDeviceLabel(
        string name,
        string deviceId,
        [FromBody] UpdateAdminDeviceLabelRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest("label is required.");

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        await accounts.UpdateDeviceName(account, deviceId, request.Label.Trim());
        return NoContent();
    }

    [HttpPost("{name}/devices/{deviceId}/sessions/revoke")]
    [AskPermission(PermissionKeys.AuthSessionsManage)]
    public async Task<IActionResult> RevokeAccountDeviceSessions(string name, string deviceId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        try
        {
            await accounts.DeleteDevice(account, deviceId);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpDelete("{name}/devices/{deviceId}")]
    [AskPermission(PermissionKeys.AccountDevicesManage)]
    public async Task<IActionResult> DeleteAccountDevice(string name, string deviceId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        try
        {
            await accounts.DeleteDevice(account, deviceId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("{name}/sessions")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAuthSession>>> ListAccountSessions(
        string name,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] SessionType? type = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] bool includeChildren = false,
        [FromQuery] bool activeOnly = false
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var query = db.AuthSessions
            .AsNoTracking()
            .Where(session => session.AccountId == account.Id);

        if (!includeChildren)
            query = query.Where(session => session.ParentSessionId == null);
        if (type.HasValue)
            query = query.Where(session => session.Type == type.Value);
        if (clientId.HasValue)
            query = query.Where(session => session.ClientId == clientId.Value);
        if (activeOnly)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            query = query.Where(session => session.ExpiredAt == null || session.ExpiredAt > now);
        }

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var sessions = await query
            .OrderByDescending(x => x.LastGrantedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        if (sessionIds.Count > 0)
        {
            var childrenCounts = await db.AuthSessions
                .AsNoTracking()
                .Where(s => s.ParentSessionId.HasValue && sessionIds.Contains(s.ParentSessionId.Value))
                .GroupBy(s => s.ParentSessionId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            foreach (var session in sessions)
                session.ChildrenCount = childrenCounts.GetValueOrDefault(session.Id, 0);
        }

        return Ok(sessions);
    }

    [HttpGet("{name}/sessions/{sessionId:guid}/children")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAuthSession>>> ListAccountSessionChildren(
        string name,
        Guid sessionId,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var parentSession = await db.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.AccountId == account.Id);
        if (parentSession is null)
            return NotFound();

        var query = db.AuthSessions
            .AsNoTracking()
            .Where(s => s.ParentSessionId == sessionId && s.AccountId == account.Id);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var sessions = await query
            .OrderByDescending(x => x.LastGrantedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var childIds = sessions.Select(s => s.Id).ToList();
        if (childIds.Count > 0)
        {
            var childrenCounts = await db.AuthSessions
                .AsNoTracking()
                .Where(s => s.ParentSessionId.HasValue && childIds.Contains(s.ParentSessionId.Value))
                .GroupBy(s => s.ParentSessionId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            foreach (var session in sessions)
                session.ChildrenCount = childrenCounts.GetValueOrDefault(session.Id, 0);
        }

        return Ok(sessions);
    }

    [HttpDelete("{name}/sessions/{sessionId:guid}")]
    [AskPermission(PermissionKeys.AuthSessionsManage)]
    public async Task<IActionResult> RevokeAccountSession(string name, Guid sessionId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        try
        {
            await accounts.DeleteSession(account, sessionId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("{name}/contacts")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAccountContact>>> ListAccountContacts(string name)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contacts = await db.AccountContacts
            .AsNoTracking()
            .Where(c => c.AccountId == account.Id)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Type)
            .ThenBy(c => c.Content)
            .ToListAsync();

        return Ok(contacts);
    }

    [HttpPost("{name}/contacts")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> CreateAccountContact(
        string name,
        [FromBody] AdminAccountContactRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required.");

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await accounts.CreateContactMethod(account, request.Type, request.Content.Trim());
        return Ok(contact);
    }

    [HttpPatch("{name}/contacts/{contactId:guid}")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> UpdateAccountContact(
        string name,
        Guid contactId,
        [FromBody] UpdateAdminAccountContactRequest request
    )
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        var typeChanged = request.Type.HasValue && contact.Type != request.Type.Value;
        var contentChanged = request.Content is not null && !string.Equals(contact.Content, request.Content, StringComparison.Ordinal);

        if (request.Type.HasValue)
            contact.Type = request.Type.Value;
        if (request.Content is not null)
            contact.Content = request.Content.Trim();

        if (typeChanged || contentChanged)
            contact.VerifiedAt = null;

        db.AccountContacts.Update(contact);
        await db.SaveChangesAsync();
        return Ok(contact);
    }

    [HttpPost("{name}/contacts/{contactId:guid}/verify/request")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> RequestAccountContactVerification(string name, Guid contactId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        try
        {
            await accounts.RequestContactVerification(account, contact);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok(contact);
    }

    [HttpPost("{name}/contacts/{contactId:guid}/verify")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> VerifyAccountContact(
        string name,
        Guid contactId,
        [FromBody] AdminContactVerificationRequest? request = null
    )
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var verifiedAt = request?.VerifiedAt ?? SystemClock.Instance.GetCurrentInstant();
        var updated = await accounts.MarkContactMethodVerified(account.Id, contactId, verifiedAt);
        if (!updated)
            return NotFound();

        var contact = await db.AccountContacts
            .AsNoTracking()
            .FirstAsync(c => c.AccountId == account.Id && c.Id == contactId);
        return Ok(contact);
    }

    [HttpDelete("{name}/contacts/{contactId:guid}/verify")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> UnverifyAccountContact(string name, Guid contactId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        contact.VerifiedAt = null;
        db.AccountContacts.Update(contact);
        await db.SaveChangesAsync();
        return Ok(contact);
    }

    [HttpPost("{name}/contacts/{contactId:guid}/primary")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> SetPrimaryAccountContact(string name, Guid contactId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        contact = await accounts.SetContactMethodPrimary(account, contact);
        return Ok(contact);
    }

    [HttpPost("{name}/contacts/{contactId:guid}/visibility")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<ActionResult<SnAccountContact>> SetAccountContactVisibility(
        string name,
        Guid contactId,
        [FromBody] SetAdminContactVisibilityRequest request
    )
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        contact = await accounts.SetContactMethodPublic(account, contact, request.IsPublic);
        return Ok(contact);
    }

    [HttpDelete("{name}/contacts/{contactId:guid}")]
    [AskPermission(PermissionKeys.AccountContactsManage)]
    public async Task<IActionResult> DeleteAccountContact(string name, Guid contactId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var contact = await db.AccountContacts
            .FirstOrDefaultAsync(c => c.AccountId == account.Id && c.Id == contactId);
        if (contact is null)
            return NotFound();

        await accounts.DeleteContactMethod(account, contact);
        return NoContent();
    }

    [HttpGet("{name}/factors")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<AccountAuthFactorSummary>>> ListAccountAuthFactors(string name)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var factors = await db.AccountAuthFactors
            .AsNoTracking()
            .Where(f => f.AccountId == account.Id)
            .OrderBy(f => f.Type)
            .ThenByDescending(f => f.EnabledAt)
            .ToListAsync();

        return Ok(factors.Select(ToAuthFactorSummary).ToList());
    }

    [HttpPost("{name}/factors")]
    [AskPermission(PermissionKeys.AuthFactorsManage)]
    public async Task<ActionResult<AccountAuthFactorSummary>> CreateAccountAuthFactor(
        string name,
        [FromBody] AdminAccountAuthFactorRequest request
    )
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        if (await accounts.CheckAuthFactorExists(account, request.Type))
            return BadRequest($"Auth factor with type {request.Type} already exists.");

        try
        {
            var factor = await accounts.CreateAuthFactor(account, request.Type, request.Secret);
            if (factor is null)
                return BadRequest("Invalid factor request.");

            if (request.Enable && factor.EnabledAt is null)
                factor = await accounts.EnableAuthFactor(factor, request.Code);

            return Ok(ToAuthFactorSummary(factor));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{name}/factors/{factorId:guid}/enable")]
    [AskPermission(PermissionKeys.AuthFactorsManage)]
    public async Task<ActionResult<AccountAuthFactorSummary>> EnableAccountAuthFactor(
        string name,
        Guid factorId,
        [FromBody] string? code
    )
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var factor = await db.AccountAuthFactors
            .FirstOrDefaultAsync(f => f.AccountId == account.Id && f.Id == factorId);
        if (factor is null)
            return NotFound();

        try
        {
            factor = await accounts.EnableAuthFactor(factor, code);
            return Ok(ToAuthFactorSummary(factor));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{name}/factors/{factorId:guid}/disable")]
    [AskPermission(PermissionKeys.AuthFactorsManage)]
    public async Task<ActionResult<AccountAuthFactorSummary>> DisableAccountAuthFactor(string name, Guid factorId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var factor = await db.AccountAuthFactors
            .FirstOrDefaultAsync(f => f.AccountId == account.Id && f.Id == factorId);
        if (factor is null)
            return NotFound();

        try
        {
            factor = await accounts.DisableAuthFactor(factor);
            return Ok(ToAuthFactorSummary(factor));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{name}/factors/password/reset")]
    [AskPermission(PermissionKeys.AuthFactorsManage)]
    public async Task<ActionResult<AccountAuthFactorSummary>> ResetAccountPasswordFactor(
        string name,
        [FromBody] AdminResetPasswordFactorRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("new_password is required.");

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var factor = await accounts.ResetPasswordFactor(account.Id, request.NewPassword);
        if (request.RevokeSessions)
            await accounts.DeleteAllSessions(account);

        return Ok(ToAuthFactorSummary(factor));
    }

    [HttpDelete("{name}/factors/{factorId:guid}")]
    [AskPermission(PermissionKeys.AuthFactorsManage)]
    public async Task<IActionResult> DeleteAccountAuthFactor(string name, Guid factorId)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var factor = await db.AccountAuthFactors
            .FirstOrDefaultAsync(f => f.AccountId == account.Id && f.Id == factorId);
        if (factor is null)
            return NotFound();

        await accounts.DeleteAuthFactor(factor);
        return NoContent();
    }

    [HttpPost("notifications")]
    [AskPermission(PermissionKeys.NotificationsSend)]
    public async Task<ActionResult<AdminMessageDispatchResponse>> SendNotification(
        [FromBody] SendAdminNotificationRequest request
    )
    {
        if (!request.BroadcastToAll && !request.AccountId.HasValue && request.AccountIds is not { Count: > 0 })
            return BadRequest("Provide account_id, account_ids, or set broadcast_to_all=true.");
        if (string.IsNullOrWhiteSpace(request.Topic))
            return BadRequest("Topic is required.");

        var targetIds = await ResolveTargetAccountIds(request.AccountId, request.AccountIds, request.BroadcastToAll);
        if (targetIds.Count == 0)
        {
            return Ok(new AdminMessageDispatchResponse
            {
                Requested = CountRequested(request.AccountId, request.AccountIds, request.BroadcastToAll),
                Resolved = 0,
                Sent = 0,
                Skipped = 0,
                BroadcastToAll = request.BroadcastToAll
            });
        }

        var meta = request.Meta is null
            ? null
            : JsonSerializer.SerializeToUtf8Bytes(request.Meta);

        await ring.SendPushNotificationToUsers(
            targetIds.Select(id => id.ToString()).ToList(),
            request.Topic,
            request.Title ?? string.Empty,
            request.Subtitle ?? string.Empty,
            request.Body ?? string.Empty,
            meta,
            request.ActionUri,
            request.IsSilent,
            request.IsSavable
        );

        return Ok(new AdminMessageDispatchResponse
        {
            Requested = CountRequested(request.AccountId, request.AccountIds, request.BroadcastToAll),
            Resolved = targetIds.Count,
            Sent = targetIds.Count,
            Skipped = 0,
            BroadcastToAll = request.BroadcastToAll
        });
    }

    [HttpPost("emails")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<AdminMessageDispatchResponse>> SendEmails(
        [FromBody] SendAdminEmailRequest request,
        [FromServices] DysonNetwork.Padlock.Mailer.EmailService mailer
    )
    {
        if (!request.BroadcastToAll && !request.AccountId.HasValue && request.AccountIds is not { Count: > 0 })
            return BadRequest("Provide account_id, account_ids, or set broadcast_to_all=true.");
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest("Subject is required.");
        if (string.IsNullOrWhiteSpace(request.HtmlBody))
            return BadRequest("Html body is required.");

        var targetIds = await ResolveTargetAccountIds(request.AccountId, request.AccountIds, request.BroadcastToAll);
        if (targetIds.Count == 0)
        {
            return Ok(new AdminMessageDispatchResponse
            {
                Requested = CountRequested(request.AccountId, request.AccountIds, request.BroadcastToAll),
                Resolved = 0,
                Sent = 0,
                Skipped = 0,
                BroadcastToAll = request.BroadcastToAll
            });
        }

        var emailContacts = await db.AccountContacts
            .AsNoTracking()
            .Where(c => targetIds.Contains(c.AccountId))
            .Where(c => c.Type == AccountContactType.Email && c.VerifiedAt != null)
            .Include(c => c.Account)
            .ToListAsync();

        var recipients = emailContacts
            .GroupBy(c => c.AccountId)
            .Select(g => g
                .OrderByDescending(c => c.IsPrimary)
                .ThenByDescending(c => c.VerifiedAt)
                .First())
            .ToList();

        foreach (var recipient in recipients)
            await mailer.SendEmailAsync(
                string.IsNullOrWhiteSpace(recipient.Account.Nick) ? recipient.Account.Name : recipient.Account.Nick,
                recipient.Content,
                request.Subject,
                request.HtmlBody
            );

        return Ok(new AdminMessageDispatchResponse
        {
            Requested = CountRequested(request.AccountId, request.AccountIds, request.BroadcastToAll),
            Resolved = targetIds.Count,
            Sent = recipients.Count,
            Skipped = targetIds.Count - recipients.Count,
            BroadcastToAll = request.BroadcastToAll
        });
    }

    [HttpGet("emails/export")]
    [AskPermission("emails.send")]
    public async Task<IActionResult> ExportEmailContactsCsv(
        [FromQuery] ExportAdminEmailContactsRequest request
    )
    {
        if (!request.BroadcastToAll && !request.AccountId.HasValue && request.AccountIds is not { Count: > 0 })
            return BadRequest("Provide account_id, account_ids, or set broadcast_to_all=true.");

        var targetIds = await ResolveTargetAccountIds(request.AccountId, request.AccountIds, request.BroadcastToAll);
        var emailContacts = await db.AccountContacts
            .AsNoTracking()
            .Where(c => targetIds.Contains(c.AccountId) && c.Type == AccountContactType.Email)
            .Include(c => c.Account)
            .ToListAsync();

        var recipients = emailContacts
            .GroupBy(c => c.AccountId)
            .Select(g => g
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.CreatedAt)
                .First())
            .OrderBy(c => string.IsNullOrWhiteSpace(c.Account.Nick) ? c.Account.Name : c.Account.Nick)
            .ToList();

        var csv = new StringBuilder();
        csv.AppendLine("EmailAddr,UserName");
        foreach (var recipient in recipients)
        {
            var userName = string.IsNullOrWhiteSpace(recipient.Account.Nick) ? recipient.Account.Name : recipient.Account.Nick;
            csv.Append(EscapeCsv(recipient.Content));
            csv.Append(',');
            csv.Append(EscapeCsv(userName));
            csv.AppendLine();
        }

        var payload = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();
        return File(payload, "text/csv; charset=utf-8", $"account-email-contacts-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    [HttpGet("punishments/created")]
    [Authorize]
    [AskPermission(PermissionKeys.PunishmentsView)]
    public async Task<ActionResult<List<SnAccountPunishment>>> GetCreatedPunishments(
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var userId = currentUser.Id;

        var query = db.Punishments
            .Where(a => a.CreatorId == userId);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var punishments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        await accounts.HydratePunishmentAccountBatch(punishments);
        return Ok(punishments);
    }

    public class CreatePunishmentRequest
    {
        [MaxLength(8192)] public string Reason { get; set; } = string.Empty;
        public Instant? ExpiredAt { get; set; }
        public PunishmentType Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
        public double? SocialCreditReduction { get; set; }
        public double? PublisherRatingReduction { get; set; }
        public List<string>? PublisherNames { get; set; }
    }

    [HttpPost("{name}/punishments")]
    [AskPermission(PermissionKeys.PunishmentsCreate)]
    public async Task<ActionResult<SnAccountPunishment>> CreatePunishment(
        string name,
        [FromBody] CreatePunishmentRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();
        var punishment = await CreatePunishmentInternal(account, currentUser, request, true);
        return Ok(punishment);
    }

    public class UpdatePunishmentRequest
    {
        [MaxLength(8192)] public string? Reason { get; set; }
        public Instant? ExpiredAt { get; set; }
        public PunishmentType? Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
    }

    [HttpPatch("{name}/punishments/{punishmentId}")]
    [AskPermission(PermissionKeys.PunishmentsUpdate)]
    public async Task<ActionResult<SnAccountPunishment>> UpdatePunishment(
        string name,
        Guid punishmentId,
        [FromBody] UpdatePunishmentRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        if (request.Reason is not null) punishment.Reason = request.Reason;
        if (request.ExpiredAt is not null) punishment.ExpiredAt = request.ExpiredAt;
        if (request.Type is not null) punishment.Type = request.Type.Value;
        if (request.BlockedPermissions is not null) punishment.BlockedPermissions = request.BlockedPermissions;
        if (punishment.CreatorId != currentUser.Id) punishment.CreatorId = currentUser.Id;

        await db.SaveChangesAsync();

        var data = new List<SnAccountPunishment> { punishment };
        await accounts.HydratePunishmentAccountBatch(data);
        return Ok(data.First());
    }

    [HttpPost("{name}/suspend")]
    [AskPermission(PermissionKeys.PunishmentsCreate)]
    public async Task<ActionResult<SnAccountPunishment>> SuspendAccount(
        string name,
        [FromBody] SuspendAccountRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        if (request.Type is not PunishmentType.BlockLogin and not PunishmentType.DisableAccount)
            return BadRequest("Suspend endpoint only supports block_login or disable_account punishments.");

        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        var punishment = await CreatePunishmentInternal(
            account,
            currentUser,
            new CreatePunishmentRequest
            {
                Reason = request.Reason,
                ExpiredAt = request.ExpiredAt,
                Type = request.Type,
                SocialCreditReduction = request.SocialCreditReduction,
                PublisherNames = request.PublisherNames,
                PublisherRatingReduction = request.PublisherRatingReduction
            },
            request.RevokeSessions
        );

        return Ok(punishment);
    }

    [HttpPost("{name}/sessions/revoke")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<IActionResult> RevokeAllSessions(string name)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        await accounts.DeleteAllSessions(account);
        return Ok();
    }

    [HttpPost("{name}/activate")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<ActionResult<SnAccount>> ActivateAccount(string name)
    {
        var account = await LookupAccountAsync(name);
        if (account is null)
            return NotFound();

        await accounts.ActivateAccountAndGrantDefaultPermissions(
            account.Id,
            SystemClock.Instance.GetCurrentInstant()
        );

        return Ok((await HydrateAccountsAsync([account])).First());
    }

    [HttpDelete("{name}/punishments/{punishmentId:guid}")]
    [AskPermission(PermissionKeys.PunishmentsDelete)]
    public async Task<ActionResult> DeletePunishment(string name, Guid punishmentId)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var remoteAccount = await profiles.GetAccountAsync(new DyGetAccountRequest { Id = account.Id.ToString() });
        if (remoteAccount is not null)
        {
            account.Language = remoteAccount.Language;
            account.Profile = remoteAccount.Profile is not null
                ? SnAccountProfile.FromProtoValue(remoteAccount.Profile)
                : null;
        }

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        var punishmentType = punishment.Type;
        db.Punishments.Remove(punishment);
        await db.SaveChangesAsync();

        var title = localizer.Get("punishmentLiftedTitle", account.Language);
        var body = localizer.Get("punishmentLiftedBody", locale: account.Language,
            args: new { type = punishmentType.ToString() });

        try
        {
            await ring.SendPushNotificationToUser(
                account.Id.ToString(),
                "account.punishment.lifted",
                title,
                null,
                body,
                isSavable: true
            );
        }
        catch
        {
            // ignored
        }

        return Ok();
    }

    [HttpDelete("{name}")]
    [AskPermission(PermissionKeys.AccountsDeletion)]
    public async Task<IActionResult> AdminDeleteAccount(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();
        await accounts.DeleteAccount(account);
        return Ok();
    }

    private async Task<SnAccount?> LookupAccountAsync(string identifier)
    {
        if (Guid.TryParse(identifier, out var accountId))
            return await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);

        return await accounts.LookupAccount(identifier);
    }

    private async Task<List<SnAccount>> HydrateAccountsAsync(List<SnAccount> localAccounts)
    {
        if (localAccounts.Count == 0)
            return [];

        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(localAccounts.Select(a => a.Id.ToString()));

        var remoteAccounts = await profiles.GetAccountBatchAsync(request);
        var remoteLookup = remoteAccounts.Accounts
            .Select(SnAccount.FromProtoValue)
            .ToDictionary(a => a.Id);

        return localAccounts.Select(localAccount =>
        {
            if (!remoteLookup.TryGetValue(localAccount.Id, out var remoteAccount))
                return localAccount;

            remoteAccount.Contacts = localAccount.Contacts;
            remoteAccount.AuthFactors = localAccount.AuthFactors;
            return remoteAccount;
        }).ToList();
    }

    private static SnAccountPunishment? SelectMostSeverePunishment(IEnumerable<SnAccountPunishment> punishments)
    {
        var priority = new Dictionary<PunishmentType, int>
        {
            { PunishmentType.DisableAccount, 0 },
            { PunishmentType.BlockLogin, 1 },
            { PunishmentType.PermissionModification, 2 },
            { PunishmentType.Strike, 3 }
        };

        return punishments.MinBy(p => priority.GetValueOrDefault(p.Type, 99));
    }

    private static AccountAuthFactorSummary ToAuthFactorSummary(SnAccountAuthFactor factor)
    {
        return new AccountAuthFactorSummary
        {
            Id = factor.Id,
            Type = factor.Type,
            Trustworthy = factor.Trustworthy,
            HasSecret = !string.IsNullOrWhiteSpace(factor.Secret),
            Config = factor.Config,
            EnabledAt = factor.EnabledAt,
            ExpiredAt = factor.ExpiredAt,
            CreatedAt = factor.CreatedAt,
            UpdatedAt = factor.UpdatedAt
        };
    }

    private async Task<List<Guid>> ResolveTargetAccountIds(Guid? accountId, List<Guid>? accountIds, bool broadcastToAll)
    {
        var requestedIds = new HashSet<Guid>();
        if (accountId.HasValue)
            requestedIds.Add(accountId.Value);
        if (accountIds is { Count: > 0 })
            foreach (var id in accountIds)
                requestedIds.Add(id);

        var query = db.Accounts
            .AsNoTracking()
            .Where(a => a.DeletedAt == null);

        if (!broadcastToAll)
            query = query.Where(a => requestedIds.Contains(a.Id));

        return await query
            .Select(a => a.Id)
            .Distinct()
            .ToListAsync();
    }

    private static int CountRequested(Guid? accountId, List<Guid>? accountIds, bool broadcastToAll)
    {
        if (broadcastToAll)
            return 0;

        var requestedIds = new HashSet<Guid>();
        if (accountId.HasValue)
            requestedIds.Add(accountId.Value);
        if (accountIds is { Count: > 0 })
            foreach (var id in accountIds)
                requestedIds.Add(id);

        return requestedIds.Count;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }

    private async Task<SnAccountPunishment> CreatePunishmentInternal(
        SnAccount account,
        SnAccount currentUser,
        CreatePunishmentRequest request,
        bool revokeSessions
    )
    {
        var punishment = new SnAccountPunishment
        {
            AccountId = account.Id,
            CreatorId = currentUser.Id,
            Reason = request.Reason,
            ExpiredAt = request.ExpiredAt,
            Type = request.Type,
            BlockedPermissions = request.BlockedPermissions
        };

        db.Punishments.Add(punishment);
        await db.SaveChangesAsync();

        if (revokeSessions && request.Type is PunishmentType.BlockLogin or PunishmentType.DisableAccount)
            await accounts.DeleteAllSessions(account);

        var title = request.Type switch
        {
            PunishmentType.PermissionModification => localizer.Get("punishmentTitlePermissionModification",
                account.Language),
            PunishmentType.BlockLogin => localizer.Get("punishmentTitleBlockLogin", account.Language),
            PunishmentType.DisableAccount => localizer.Get("punishmentTitleDisableAccount", account.Language),
            _ => localizer.Get("punishmentTitleStrike", account.Language)
        };
        var body = request.ExpiredAt.HasValue
            ? localizer.Get("punishmentBodyWithExpiry", locale: account.Language,
                args: new { reason = request.Reason, expiredAt = request.ExpiredAt.Value.ToString() })
            : localizer.Get("punishmentBody", locale: account.Language, args: new { reason = request.Reason });

        if (request.SocialCreditReduction is > 0)
        {
            await socialCredits.AddRecordAsync(new DyAddSocialCreditRecordRequest
            {
                AccountId = account.Id.ToString(),
                Delta = -request.SocialCreditReduction.Value,
                Reason = $"{title} {request.Reason}",
                ReasonType = "punishments",
                ExpiredAt = request.ExpiredAt?.ToTimestamp() ?? SystemClock.Instance.GetCurrentInstant()
                    .Plus(Duration.FromDays(365)).ToTimestamp(),
            });
        }

        if (request.PublisherRatingReduction is > 0 && request.PublisherNames is { Count: > 0 })
        {
            foreach (var publisherName in request.PublisherNames)
            {
                try
                {
                    var publisherResp = await publishers.GetPublisherAsync(
                        new DyGetPublisherRequest { Name = publisherName });
                    var publisherId = publisherResp.Publisher.Id;
                    await publisherRatings.AddRecordAsync(new DyAddPublisherRatingRecordRequest
                    {
                        PublisherId = publisherId,
                        Delta = -request.PublisherRatingReduction.Value,
                        Reason = $"{title} {request.Reason}",
                        ReasonType = "punishments",
                    });
                }
                catch
                {
                    // ignored - publisher may not exist
                }
            }
        }

        try
        {
            await ring.SendPushNotificationToUser(
                account.Id.ToString(),
                "account.punishment",
                title,
                localizer.Get("punishmentTitle", account.Language),
                body,
                isSavable: true
            );
        }
        catch
        {
            // ignored
        }

        var data = new List<SnAccountPunishment> { punishment };
        await accounts.HydratePunishmentAccountBatch(data);
        return data.First();
    }
}
