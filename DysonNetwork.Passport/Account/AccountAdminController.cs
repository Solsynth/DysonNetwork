using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Account.Presences;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    SocialCreditService socialCreditService,
    AccountEventService accountEventService,
    AccountService accountService,
    SteamPresenceService steamPresenceService
) : ControllerBase
{
    [HttpPost("{name}/credits")]
    [AskPermission("credits.validate.perform")]
    public async Task<IActionResult> InvalidateSocialCreditCache(string name)
    {
        await socialCreditService.InvalidateCache();
        return Ok();
    }

    [HttpPost("presences/steam/scan")]
    [AskPermission("accounts.statuses.update")]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresences()
    {
        var userIds = await accountEventService.GetPresenceConnectedUsersAsync("steam");
        var result = await steamPresenceService.ScanAndUpdatePresencesAsync(userIds);
        return Ok(result);
    }

    [HttpPost("presences/steam/scan/{identifier}")]
    [AskPermission("accounts.statuses.update")]
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
    [AskPermission("accounts.statuses.update")]
    public async Task<ActionResult<SteamPresenceService.SteamPresenceScanResult>> ScanSteamPresenceBySteamId(
        string steamId
    )
    {
        var result = await steamPresenceService.ScanAndUpdatePresencesAsync([], steamId);
        return Ok(result);
    }

    [HttpPost("presences/steam/scan/stages/{stage}")]
    [AskPermission("accounts.statuses.update")]
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
            var activeActivities = await accountEventService.GetActiveActivities(userId);
            var hasActivePresence = activeActivities.Any();

            var shouldInclude = stage switch
            {
                PresenceUpdateStage.Active => isOnline && hasActivePresence,
                PresenceUpdateStage.Maybe => isOnline && !hasActivePresence,
                PresenceUpdateStage.Cold => !isOnline,
                _ => false
            };

            if (shouldInclude)
                filteredUserIds.Add(userId);
        }

        return filteredUserIds;
    }
}
