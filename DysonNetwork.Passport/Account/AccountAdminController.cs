using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Account.Presences;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    AppDatabase db,
    SocialCreditService socialCreditService,
    AccountEventService accountEventService,
    AccountService accountService,
    AccountBoardService boardService,
    SteamPresenceService steamPresenceService,
    DyProfileService.DyProfileServiceClient profiles,
    DyAccountService.DyAccountServiceClient accountGrpc,
    MagicSpellService magicSpells
) : ControllerBase
{
    public class AccountAuthFactorSummary
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public int Trustworthy { get; set; }
        public bool HasSecret { get; set; }
        public Dictionary<string, object?>? Config { get; set; }
        public Instant? EnabledAt { get; set; }
        public Instant? ExpiredAt { get; set; }
        public Instant CreatedAt { get; set; }
        public Instant UpdatedAt { get; set; }
    }

    public class AdminAccountSummaryResponse
    {
        public SnAccount Account { get; set; } = null!;
        public SnAccountStatus Status { get; set; } = null!;
        public int BadgeCount { get; set; }
        public int ActiveActivityCount { get; set; }
    }

    public class AdminAccountDetailResponse
    {
        public SnAccount Account { get; set; } = null!;
        public SnAccountStatus Status { get; set; } = null!;
        public List<SnPresenceActivity> Activities { get; set; } = [];
        public List<SnAccountContact> Contacts { get; set; } = [];
        public List<AccountAuthFactorSummary> AuthFactors { get; set; } = [];
        public List<SnAccountBadge> Badges { get; set; } = [];
        public int BadgeCount { get; set; }
        public List<SnAccountBoardItem> Board { get; set; } = [];
    }

    public class AdminAccountActivityMetricsResponse
    {
        public Instant CalculatedAt { get; set; }
        public Instant CurrentDayStartedAt { get; set; }
        public int DailyActiveUsers { get; set; }
        public int WeeklyActiveUsers { get; set; }
        public int MonthlyActiveUsers { get; set; }
        public int PreviousDailyActiveUsers { get; set; }
        public int PreviousWeeklyActiveUsers { get; set; }
        public int PreviousMonthlyActiveUsers { get; set; }
        public int NewAccountsToday { get; set; }
        public int NewAccountsThisWeek { get; set; }
        public int NewAccountsThisMonth { get; set; }
        public int TotalProfiledAccounts { get; set; }
    }

    public class UpdateAccountVerificationRequest
    {
        public VerificationMarkType Type { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? VerifiedBy { get; set; }
    }

    public class AdminBadgeRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public Dictionary<string, object?>? Meta { get; set; }
        public Instant? ActivatedAt { get; set; }
        public Instant? ExpiredAt { get; set; }
    }

    public class CreateAdminMagicSpellRequest
    {
        [Required] public MagicSpellType? Type { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public Instant? ExpiresAt { get; set; }
        public Instant? AffectedAt { get; set; }
        [MaxLength(1024)] public string? Code { get; set; }
        public bool PreventRepeat { get; set; }
        public bool SendEmail { get; set; } = true;
        public bool BypassVerify { get; set; } = true;
    }

    public class ResendAdminMagicSpellRequest
    {
        public bool BypassVerify { get; set; } = true;
    }

    [HttpGet("metrics/activity")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<AdminAccountActivityMetricsResponse>> GetActivityMetrics()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var currentDayStartedAt = now.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var currentWeekStartedAt = currentDayStartedAt - Duration.FromDays(6);
        var currentMonthStartedAt = currentDayStartedAt - Duration.FromDays(29);
        var previousDayStartedAt = currentDayStartedAt - Duration.FromDays(1);
        var previousWeekStartedAt = currentWeekStartedAt - Duration.FromDays(7);
        var previousMonthStartedAt = currentMonthStartedAt - Duration.FromDays(30);

        var profiles = db.AccountProfiles.AsNoTracking();
        var dailyActiveUsers = await profiles.CountAsync(p => p.LastSeenAt >= currentDayStartedAt);
        var weeklyActiveUsers = await profiles.CountAsync(p => p.LastSeenAt >= currentWeekStartedAt);
        var monthlyActiveUsers = await profiles.CountAsync(p => p.LastSeenAt >= currentMonthStartedAt);
        var previousDailyActiveUsers = await profiles.CountAsync(p =>
            p.LastSeenAt >= previousDayStartedAt && p.LastSeenAt < currentDayStartedAt);
        var previousWeeklyActiveUsers = await profiles.CountAsync(p =>
            p.LastSeenAt >= previousWeekStartedAt && p.LastSeenAt < currentWeekStartedAt);
        var previousMonthlyActiveUsers = await profiles.CountAsync(p =>
            p.LastSeenAt >= previousMonthStartedAt && p.LastSeenAt < currentMonthStartedAt);
        var newAccountsToday = await profiles.CountAsync(p => p.CreatedAt >= currentDayStartedAt);
        var newAccountsThisWeek = await profiles.CountAsync(p => p.CreatedAt >= currentWeekStartedAt);
        var newAccountsThisMonth = await profiles.CountAsync(p => p.CreatedAt >= currentMonthStartedAt);
        var totalProfiledAccounts = await profiles.CountAsync();

        return Ok(new AdminAccountActivityMetricsResponse
        {
            CalculatedAt = now,
            CurrentDayStartedAt = currentDayStartedAt,
            DailyActiveUsers = dailyActiveUsers,
            WeeklyActiveUsers = weeklyActiveUsers,
            MonthlyActiveUsers = monthlyActiveUsers,
            PreviousDailyActiveUsers = previousDailyActiveUsers,
            PreviousWeeklyActiveUsers = previousWeeklyActiveUsers,
            PreviousMonthlyActiveUsers = previousMonthlyActiveUsers,
            NewAccountsToday = newAccountsToday,
            NewAccountsThisWeek = newAccountsThisWeek,
            NewAccountsThisMonth = newAccountsThisMonth,
            TotalProfiledAccounts = totalProfiledAccounts
        });
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

        var page = offset / take;
        var pageOffset = offset % take;
        var request = new DyListAccountsRequest
        {
            PageSize = Math.Min(take + pageOffset, 500),
            PageToken = page.ToString(),
            Filter = query ?? string.Empty,
            OrderBy = orderBy ?? "created_at_desc"
        };

        var response = await profiles.ListAccountsAsync(request);
        Response.Headers.Append("X-Total", response.TotalSize.ToString());

        var accounts = response.Accounts
            .Skip(pageOffset)
            .Take(take)
            .Select(SnAccount.FromProtoValue)
            .ToList();
        if (accounts.Count == 0)
            return Ok(new List<AdminAccountSummaryResponse>());

        var accountIds = accounts.Select(a => a.Id).ToList();
        var statuses = await accountEventService.GetStatuses(accountIds);
        var activities = await accountEventService.GetActiveActivitiesBatch(accountIds);
        var badgeCounts = await db.Badges
            .AsNoTracking()
            .Where(b => accountIds.Contains(b.AccountId))
            .GroupBy(b => b.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        var result = accounts.Select(account => new AdminAccountSummaryResponse
        {
            Account = account,
            Status = statuses.GetValueOrDefault(account.Id) ?? CreateOfflineStatus(account.Id),
            BadgeCount = badgeCounts.GetValueOrDefault(account.Id),
            ActiveActivityCount = activities.GetValueOrDefault(account.Id)?.Count ?? 0
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{identifier}")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<AdminAccountDetailResponse>> GetAccount(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var status = await accountEventService.GetStatus(account.Id);
        var activities = await accountEventService.GetActiveActivities(account.Id);
        var badges = await db.Badges
            .AsNoTracking()
            .Where(b => b.AccountId == account.Id)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
        var contacts = await accountGrpc.ListContactsAsync(new DyListContactsRequest
        {
            AccountId = account.Id.ToString(),
            VerifiedOnly = false,
            Type = DyAccountContactType.Unspecified
        });
        var factors = await accountGrpc.ListAuthFactorsAsync(new DyListAuthFactorsRequest
        {
            AccountId = account.Id.ToString(),
            ActiveOnly = false
        });

        var board = await boardService.GetBoardAsync(account.Id);

        return Ok(new AdminAccountDetailResponse
        {
            Account = account,
            Status = status,
            Activities = activities,
            Contacts = contacts.Contacts.Select(SnAccountContact.FromProtoValue).ToList(),
            AuthFactors = factors.Factors.Select(ToAuthFactorSummary).ToList(),
            Badges = badges,
            BadgeCount = badges.Count,
            Board = board
        });
    }

    [HttpGet("{identifier}/contacts")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAccountContact>>> GetAccountContacts(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var contacts = await accountGrpc.ListContactsAsync(new DyListContactsRequest
        {
            AccountId = account.Id.ToString(),
            VerifiedOnly = false,
            Type = DyAccountContactType.Unspecified
        });

        return Ok(contacts.Contacts.Select(SnAccountContact.FromProtoValue).ToList());
    }

    [HttpGet("{identifier}/spells")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnMagicSpell>>> ListAccountMagicSpells(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        return Ok(await db.MagicSpells
            .AsNoTracking()
            .Where(spell => spell.AccountId == account.Id)
            .OrderByDescending(spell => spell.CreatedAt)
            .ToListAsync());
    }

    [HttpPost("{identifier}/spells")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<ActionResult<SnMagicSpell>> CreateAccountMagicSpell(
        string identifier,
        [FromBody] CreateAdminMagicSpellRequest request
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();
        if (!request.Type.HasValue || !Enum.IsDefined(request.Type.Value))
            return BadRequest("A supported magic spell type is required.");
        if (request.Type == MagicSpellType.AccountDeactivation)
            return BadRequest("Account deactivation magic spells cannot be sent by email.");

        var spell = await magicSpells.CreateMagicSpell(
            account,
            request.Type.Value,
            request.Meta ?? [],
            request.ExpiresAt,
            request.AffectedAt,
            request.Code,
            request.PreventRepeat
        );
        if (request.SendEmail)
            await magicSpells.ResendMagicSpell(spell, request.BypassVerify);

        return Created($"/api/admin/accounts/{account.Id}/spells/{spell.Id}", spell);
    }

    [HttpPost("{identifier}/spells/{spellId:guid}/resend")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<IActionResult> ResendAccountMagicSpell(
        string identifier,
        Guid spellId,
        [FromBody] ResendAdminMagicSpellRequest? request = null
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var spell = await db.MagicSpells
            .FirstOrDefaultAsync(candidate => candidate.Id == spellId && candidate.AccountId == account.Id);
        if (spell is null)
            return NotFound();
        if (spell.Type == MagicSpellType.AccountDeactivation)
            return BadRequest("Account deactivation magic spells cannot be sent by email.");

        await magicSpells.ResendMagicSpell(spell, request?.BypassVerify ?? true);
        return NoContent();
    }

    [HttpDelete("{identifier}/spells/{spellId:guid}")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<IActionResult> DeleteAccountMagicSpell(string identifier, Guid spellId)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var spell = await db.MagicSpells
            .FirstOrDefaultAsync(candidate => candidate.Id == spellId && candidate.AccountId == account.Id);
        if (spell is null)
            return NotFound();

        db.MagicSpells.Remove(spell);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{identifier}/factors")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<AccountAuthFactorSummary>>> GetAccountAuthFactors(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var factors = await accountGrpc.ListAuthFactorsAsync(new DyListAuthFactorsRequest
        {
            AccountId = account.Id.ToString(),
            ActiveOnly = false
        });

        return Ok(factors.Factors.Select(ToAuthFactorSummary).ToList());
    }

    [HttpPost("{identifier}/verification")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<ActionResult<SnVerificationMark>> SetAccountVerification(
        string identifier,
        [FromBody] UpdateAccountVerificationRequest request
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var profile = await accountService.GetOrCreateAccountProfileAsync(account.Id);
        profile.Verification = new SnVerificationMark
        {
            Type = request.Type,
            Title = request.Title,
            Description = request.Description,
            VerifiedBy = request.VerifiedBy
        };

        db.AccountProfiles.Update(profile);
        await db.SaveChangesAsync();
        return Ok(profile.Verification);
    }

    [HttpDelete("{identifier}/verification")]
    [AskPermission(PermissionKeys.AccountsManage)]
    public async Task<IActionResult> ClearAccountVerification(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var profile = await accountService.GetOrCreateAccountProfileAsync(account.Id);
        profile.Verification = null;

        db.AccountProfiles.Update(profile);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{identifier}/badges")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAccountBadge>>> GetAccountBadges(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var badges = await db.Badges
            .AsNoTracking()
            .Where(b => b.AccountId == account.Id)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
        return Ok(badges);
    }

    [HttpPost("{identifier}/badges")]
    [AskPermission(PermissionKeys.ProgressionBadgesManage)]
    public async Task<ActionResult<SnAccountBadge>> GrantBadge(
        string identifier,
        [FromBody] AdminBadgeRequest request
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var badge = await accountService.GrantBadge(account, new SnAccountBadge
        {
            Type = request.Type,
            Label = request.Label,
            Caption = request.Caption,
            Meta = request.Meta ?? new Dictionary<string, object?>(),
            ActivatedAt = request.ActivatedAt,
            ExpiredAt = request.ExpiredAt
        });

        return Ok(badge);
    }

    [HttpPost("{identifier}/badges/{badgeId:guid}/activate")]
    [AskPermission(PermissionKeys.ProgressionBadgesManage)]
    public async Task<IActionResult> ActivateBadge(string identifier, Guid badgeId)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        await accountService.ActiveBadge(account, badgeId);
        return Ok();
    }

    [HttpDelete("{identifier}/badges/{badgeId:guid}")]
    [AskPermission(PermissionKeys.ProgressionBadgesManage)]
    public async Task<IActionResult> RevokeBadge(string identifier, Guid badgeId)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        await accountService.RevokeBadge(account, badgeId);
        return NoContent();
    }

    // ── Board Management ──

    public class AdminBoardItemRequest
    {
        public Guid? Id { get; set; }
        public int Order { get; set; }
        public SnAccountBoardItemKind Kind { get; set; }
        [MaxLength(256)] public string? WidgetKey { get; set; }
        public Guid? CustomAppId { get; set; }
        [MaxLength(256)] public string? CustomAppWidgetKey { get; set; }
        public bool IsEnabled { get; set; } = true;
        public Dictionary<string, object?>? Payload { get; set; }

        public SnAccountBoardItem ToModel()
        {
            return new SnAccountBoardItem
            {
                Id = Id ?? Guid.Empty,
                Order = Order,
                Kind = Kind,
                WidgetKey = WidgetKey,
                CustomAppId = CustomAppId,
                CustomAppWidgetKey = CustomAppWidgetKey,
                IsEnabled = IsEnabled,
                Payload = Payload ?? []
            };
        }
    }

    public class AdminPushBoardPayloadRequest
    {
        public Dictionary<string, object?> Payload { get; set; } = [];
    }

    [HttpGet("{identifier}/board")]
    [AskPermission(PermissionKeys.AccountsView)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> GetAccountBoard(string identifier)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        return Ok(await boardService.GetBoardAsync(account.Id));
    }

    [HttpPut("{identifier}/board")]
    [AskPermission(PermissionKeys.AccountsBoardManage)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> ReplaceAccountBoard(
        string identifier,
        [FromBody] List<AdminBoardItemRequest> request
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        try
        {
            var result = await boardService.ReplaceBoardAsync(account.Id, request.Select(x => x.ToModel()));
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/board/items/{itemId:guid}/payload")]
    [AskPermission(PermissionKeys.AccountsBoardManage)]
    public async Task<ActionResult<SnAccountBoardItem>> PushBoardItemPayload(
        string identifier,
        Guid itemId,
        [FromBody] AdminPushBoardPayloadRequest request
    )
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        try
        {
            var result = await boardService.AdminUpdateBoardItemPayloadAsync(
                account.Id, itemId, request.Payload);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{identifier}/board/items/{itemId:guid}")]
    [AskPermission(PermissionKeys.AccountsBoardManage)]
    public async Task<IActionResult> RemoveBoardItem(string identifier, Guid itemId)
    {
        var account = await LookupAccountAsync(identifier);
        if (account is null)
            return NotFound();

        var item = await db.AccountBoardItems
            .Where(x => x.Id == itemId && x.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (item is null)
            return NotFound();

        db.AccountBoardItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{name}/credits")]
    [AskPermission(PermissionKeys.CreditsValidatePerform)]
    public async Task<IActionResult> InvalidateSocialCreditCache(string name)
    {
        await socialCreditService.InvalidateCache();
        return Ok();
    }

    [HttpPost("presences/steam/scan")]
    [AskPermission(PermissionKeys.AccountsStatusesUpdate)]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresences()
    {
        var userIds = await accountEventService.GetPresenceConnectedUsersAsync("steam");
        var result = await steamPresenceService.ScanAndUpdatePresencesAsync(userIds);
        return Ok(result);
    }

    [HttpPost("presences/steam/scan/{identifier}")]
    [AskPermission(PermissionKeys.AccountsStatusesUpdate)]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresenceForAccount(
        string identifier
    )
    {
        var account = Guid.TryParse(identifier, out var accountId)
            ? await accountService.GetAccount(accountId)
            : await accountService.LookupAccount(identifier);
        if (account is null)
            return NotFound();

        var result = await steamPresenceService.ScanAndUpdatePresencesAsync([account.Id]);
        return Ok(result);
    }

    [HttpPost("presences/steam/scan-by-steam-id/{steamId}")]
    [AskPermission(PermissionKeys.AccountsStatusesUpdate)]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresenceBySteamId(
        string steamId
    )
    {
        var result = await steamPresenceService.ScanAndUpdatePresencesAsync([], steamId);
        return Ok(result);
    }

    [HttpPost("presences/steam/scan/stages/{stage}")]
    [AskPermission(PermissionKeys.AccountsStatusesUpdate)]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresencesByStage(
        string stage
    )
    {
        if (!Enum.TryParse<PresenceUpdateStage>(stage, true, out var parsedStage))
            return BadRequest("Invalid stage. Expected one of: active, maybe, cold");

        var userIds = await GetSteamPresenceUsersForStageAsync(parsedStage);
        var result = await steamPresenceService.ScanAndUpdatePresencesAsync(userIds);
        return Ok(result);
    }

    private async Task<List<Guid>> GetSteamPresenceUsersForStageAsync(PresenceUpdateStage stage)
    {
        var allUserIds = await accountEventService.GetPresenceConnectedUsersAsync("steam");
        if (allUserIds.Count == 0)
            return [];

        var onlineStatuses = await accountEventService.GetAccountIsConnectedBatch(allUserIds);
        var filteredUserIds = new List<Guid>();

        foreach (var userId in allUserIds)
        {
            var isOnline = onlineStatuses.GetValueOrDefault(userId.ToString(), false);
            var shouldInclude = stage switch
            {
                PresenceUpdateStage.Active => isOnline,
                PresenceUpdateStage.Maybe => false,
                PresenceUpdateStage.Cold => !isOnline,
                _ => false
            };

            if (shouldInclude)
                filteredUserIds.Add(userId);
        }

        return filteredUserIds;
    }

    private async Task<SnAccount?> LookupAccountAsync(string identifier)
    {
        if (Guid.TryParse(identifier, out var accountId))
            return await accountService.GetAccount(accountId);

        return await accountService.LookupAccount(identifier);
    }

    private static SnAccountStatus CreateOfflineStatus(Guid accountId)
    {
        return new SnAccountStatus
        {
            AccountId = accountId,
            Attitude = StatusAttitude.Neutral,
            IsCustomized = false,
            IsOnline = false,
            Label = "Offline",
            Type = StatusType.Default
        };
    }

    private static AccountAuthFactorSummary ToAuthFactorSummary(DyAccountAuthFactor factor)
    {
        return new AccountAuthFactorSummary
        {
            Id = Guid.Parse(factor.Id),
            Type = factor.Type switch
            {
                DyAccountAuthFactorType.DyPassword => "password",
                DyAccountAuthFactorType.DyEmailCode => "email_code",
                DyAccountAuthFactorType.DyInAppCode => "in_app_code",
                DyAccountAuthFactorType.DyTimedCode => "timed_code",
                DyAccountAuthFactorType.DyPinCode => "pin_code",
                DyAccountAuthFactorType.DyPasskey => "passkey",
                _ => "unspecified"
            },
            Trustworthy = factor.Trustworthy,
            HasSecret = !string.IsNullOrWhiteSpace(factor.Secret),
            Config = factor.Config is { Count: > 0 }
                ? InfraObjectCoder.ConvertFromValueMap(factor.Config).ToDictionary()
                : null,
            EnabledAt = factor.EnabledAt?.ToInstant(),
            ExpiredAt = factor.ExpiredAt?.ToInstant(),
            CreatedAt = factor.CreatedAt?.ToInstant() ?? default,
            UpdatedAt = factor.UpdatedAt?.ToInstant() ?? default
        };
    }
}
