using DysonNetwork.Passport.Credit;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/accounts")]
[ApiFeature("accounts", Revision = 1)]
[ApiFeature("accounts.badges", Revision = 1)]
[ApiFeature("accounts.credits", Revision = 1)]
public class AccountPublicController(
    AppDatabase db,
    AccountService accountService,
    SocialCreditService socialCreditService
) : ControllerBase
{
    [HttpGet("{name}/badges")]
    [ProducesResponseType<List<SnAccountBadge>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SnAccountBadge>>> GetBadgesByName(string name)
    {
        var account = await accountService.LookupAccount(name);
        return account is null
            ? NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier))
            : await db.Badges.Where(b => b.AccountId == account.Id).ToListAsync();
    }

    [HttpGet("{name}/credits")]
    [ProducesResponseType<double>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<double>> GetSocialCredits(string name)
    {
        var account = await accountService.LookupAccount(name);

        if (account is null)
        {
            return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));
        }

        var credits = await socialCreditService.GetSocialCredit(account.Id);
        return credits;
    }
}
