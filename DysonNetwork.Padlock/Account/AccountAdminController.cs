using System.ComponentModel.DataAnnotations;
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
    [AskPermission(PermissionKeys.EmailsSend)]
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
        {
            await mailer.SendEmailAsync(
                string.IsNullOrWhiteSpace(recipient.Account.Nick) ? recipient.Account.Name : recipient.Account.Nick,
                recipient.Content,
                request.Subject,
                request.HtmlBody
            );
        }

        return Ok(new AdminMessageDispatchResponse
        {
            Requested = CountRequested(request.AccountId, request.AccountIds, request.BroadcastToAll),
            Resolved = targetIds.Count,
            Sent = recipients.Count,
            Skipped = targetIds.Count - recipients.Count,
            BroadcastToAll = request.BroadcastToAll
        });
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
