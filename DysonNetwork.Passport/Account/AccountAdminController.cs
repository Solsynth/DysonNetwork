using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Account.Presences;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    AppDatabase db,
    SocialCreditService socialCreditService,
    AccountEventService accountEventService,
    AccountService accountService,
    SteamPresenceService steamPresenceService,
    DyProfileService.DyProfileServiceClient profiles
) : ControllerBase
{
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
        public int BadgeCount { get; set; }
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
        var badgeCount = await db.Badges
            .AsNoTracking()
            .CountAsync(b => b.AccountId == account.Id);

        return Ok(new AdminAccountDetailResponse
        {
            Account = account,
            Status = status,
            Activities = activities,
            BadgeCount = badgeCount
        });
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
}
