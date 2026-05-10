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
}
