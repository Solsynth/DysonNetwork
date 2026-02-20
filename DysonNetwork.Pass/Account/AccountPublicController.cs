using DysonNetwork.Pass.Credit;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountPublicController(
    AppDatabase db,
    SocialCreditService socialCreditService,
    RemoteSubscriptionService remoteSubscription
) : ControllerBase
{
    [HttpGet("{name}")]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnAccount?>> GetByName(string name)
    {
        var accountQuery = db.Accounts
            .Include(e => e.Badges)
            .Include(e => e.Profile)
            .Include(e => e.Contacts.Where(c => c.IsPublic))
            .AsQueryable();
        
        if (Guid.TryParse(name, out var guid))
            accountQuery = accountQuery.Where(a => a.Id == guid);
        else
            accountQuery = accountQuery
                .Where(a => EF.Functions.Like(a.Name, name));
        
        var account = await accountQuery.FirstOrDefaultAsync();
        if (account is null) return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));

        // Populate PerkSubscription from Wallet service via gRPC
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the request - PerkSubscription is optional
            Console.WriteLine($"Failed to populate PerkSubscription for account {account.Id}: {ex.Message}");
        }

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
    public async Task<List<SnAccount>> SearchAccounts([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        
        var accounts = await db.Accounts
            .Include(e => e.Profile)
            .Where(a => EF.Functions.ILike(a.Name, $"%{query}%") ||
                        EF.Functions.ILike(a.Nick, $"%{query}%"))
            .Take(take)
            .ToListAsync();

        // Populate PerkSubscriptions from Wallet service via gRPC
        if (accounts.Count > 0)
        {
            try
            {
                var accountIds = accounts.Select(a => a.Id).ToList();
                var subscriptions = await remoteSubscription.GetPerkSubscriptions(accountIds);
                
                var subscriptionDict = subscriptions
                    .Where(s => s != null)
                    .ToDictionary(
                        s => Guid.Parse(s.AccountId), 
                        s => SnWalletSubscription.FromProtoValue(s).ToReference()
                    );

                foreach (var account in accounts)
                {
                    if (subscriptionDict.TryGetValue(account.Id, out var subscription))
                    {
                        account.PerkSubscription = subscription;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the request - PerkSubscription is optional
                Console.WriteLine($"Failed to populate PerkSubscriptions for search results: {ex.Message}");
            }
        }

        return accounts;
    }
}
