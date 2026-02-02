using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountPublicController(
    AppDatabase db,
    SubscriptionService subscriptions,
    SocialCreditService socialCreditService
) : ControllerBase
{
    [HttpGet("{name}")]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnAccount?>> GetByName(string name)
    {
        var account = await db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Include(e => e.Contacts.Where(c => c.IsPublic))
            .Where(a => EF.Functions.Like(a.Name, name))
            .FirstOrDefaultAsync();
        if (account is null) return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));

        var perk = await subscriptions.GetPerkSubscriptionAsync(account.Id);
        account.PerkSubscription = perk?.ToReference();

        return account;
    }

    [HttpGet("{name}/badges")]
    [ProducesResponseType<List<SnAccountBadge>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SnAccountBadge>>> GetBadgesByName(string name)
    {
        var account = await db.Accounts
            .Include(e => e.Badges)
            .Where(a => a.Name == name)
            .FirstOrDefaultAsync();
        return account is null
            ? NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier))
            : account.Badges.ToList();
    }

    [HttpGet("{name}/credits")]
    [ProducesResponseType<double>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<double>> GetSocialCredits(string name)
    {
        var account = await db.Accounts
            .Where(a => a.Name == name)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync();

        if (account is null)
        {
            return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));
        }

        var credits = await socialCreditService.GetSocialCredit(account.Id);
        return credits;
    }

    [HttpGet("search")]
    public async Task<List<SnAccount>> Search([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        return await db.Accounts
            .Include(e => e.Profile)
            .Where(a => EF.Functions.ILike(a.Name, $"%{query}%") ||
                        EF.Functions.ILike(a.Nick, $"%{query}%"))
            .Take(take)
            .ToListAsync();
    }
}
