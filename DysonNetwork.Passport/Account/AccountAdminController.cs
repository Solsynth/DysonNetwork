using DysonNetwork.Passport.Credit;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    SocialCreditService socialCreditService
) : ControllerBase
{
    [HttpPost("{name}/credits")]
    [AskPermission("credits.validate.perform")]
    public async Task<IActionResult> InvalidateSocialCreditCache(string name)
    {
        await socialCreditService.InvalidateCache();
        return Ok();
    }
}
