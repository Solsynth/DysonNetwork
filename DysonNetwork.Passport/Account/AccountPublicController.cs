using DysonNetwork.Passport.Credit;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountPublicController(
    AppDatabase db,
    AccountService accountService,
    SocialCreditService socialCreditService,
    RemoteSubscriptionService remoteSubscription,
    DyAccountService.DyAccountServiceClient accountGrpc
) : ControllerBase
{
    [HttpGet("{name}")]
    [ProducesResponseType<SnAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SnAccount?>> GetByName(string name)
    {
        SnAccount? account = null;
        if (Guid.TryParse(name, out var guid))
        {
            account = await accountService.GetAccount(guid);
        }
        else
        {
            var candidates = (await accountGrpc.SearchAccountAsync(new DySearchAccountRequest { Query = name })).Accounts;
            var hit = candidates.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                account = SnAccount.FromProtoValue(hit);
        }

        if (account is null) return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));
        await EnsureProfileAsync(account);
        account.Badges = await db.Badges.Where(b => b.AccountId == account.Id).ToListAsync();
        account.Contacts = [];

        // Populate PerkSubscription from Wallet service via gRPC
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
                account.PerkLevel = account.PerkSubscription.PerkLevel;
            }
            else
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
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

    [HttpGet("search")]
    public async Task<List<SnAccount>> SearchAccounts([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        
        var accounts = (await accountGrpc.SearchAccountAsync(new DySearchAccountRequest { Query = query })).Accounts
            .Take(take)
            .Select(SnAccount.FromProtoValue)
            .ToList();

        foreach (var account in accounts)
        {
            await EnsureProfileAsync(account);
            account.Badges = await db.Badges.Where(b => b.AccountId == account.Id).ToListAsync();
            account.Contacts = [];
        }

        // Populate PerkSubscriptions from Wallet service via gRPC
        if (accounts.Count > 0)
        {
            try
            {
                var accountIds = accounts.Select(a => a.Id).ToList();
                var subscriptions = await remoteSubscription.GetPerkSubscriptions(accountIds);
                
                var subscriptionDict = subscriptions
                    .ToDictionary(
                        s => Guid.Parse(s.AccountId), 
                        s => SnWalletSubscription.FromProtoValue(s).ToReference()
                    );

                foreach (var account in accounts)
                {
                    if (subscriptionDict.TryGetValue(account.Id, out var subscription))
                    {
                        account.PerkSubscription = subscription;
                        account.PerkLevel = subscription.PerkLevel;
                    }
                    else
                    {
                        account.PerkSubscription = null;
                        account.PerkLevel = 0;
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

    private async Task EnsureProfileAsync(SnAccount account)
    {
        if (account.Profile is not null) return;
        account.Profile = await accountService.GetOrCreateAccountProfileAsync(account.Id);
    }
}
