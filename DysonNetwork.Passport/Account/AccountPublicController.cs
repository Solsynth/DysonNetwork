using DysonNetwork.Passport.Credit;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/accounts")]
public class AccountPublicController(
    AppDatabase db,
    AccountService accountService,
    SocialCreditService socialCreditService,
    RemoteSubscriptionService remoteSubscription,
    DyAccountService.DyAccountServiceClient accountGrpc,
    RemoteAccountContactService remoteContacts,
    RemoteAccountConnectionService remoteConnections,
    AccountBoardService boardService,
    IConfiguration configuration
) : ControllerBase
{
    public class PublicAccountConnectionResponse
    {
        public string Provider { get; set; } = string.Empty;
        public string ProvidedIdentifier { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

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
        account.Contacts = (await remoteContacts.ListContactsAsync(account.Id))
            .Where(contact => contact.IsPublic)
            .ToList();

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

    [HttpGet("{name}/connections")]
    [ProducesResponseType<List<PublicAccountConnectionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PublicAccountConnectionResponse>>> GetPublicConnections(string name)
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

        var connections = await remoteConnections.ListConnectionsAsync(account.Id);
        return Ok(connections
            .Where(connection => connection.IsPublic)
            .Select(connection => new PublicAccountConnectionResponse
            {
                Provider = connection.Provider,
                ProvidedIdentifier = connection.ProvidedIdentifier,
                Url = BuildPublicConnectionUrl(connection)
            })
            .ToList());
    }

    private static string BuildPublicConnectionUrl(SnAccountConnection connection)
    {
        if (string.Equals(connection.Provider, "steam", StringComparison.OrdinalIgnoreCase))
            return $"https://steamcommunity.com/profiles/{Uri.EscapeDataString(connection.ProvidedIdentifier)}";

        if (string.Equals(connection.Provider, "github", StringComparison.OrdinalIgnoreCase) &&
            connection.Meta.TryGetValue("preferred_username", out var preferredUsername) &&
            GetMetadataString(preferredUsername) is { } username && !string.IsNullOrWhiteSpace(username))
            return $"https://github.com/{Uri.EscapeDataString(username)}";

        return string.Empty;
    }

    private static string? GetMetadataString(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.String } element)
            return element.GetString();

        if (value is not string stringValue)
            return null;

        try
        {
            return JsonSerializer.Deserialize<string>(stringValue) ?? stringValue;
        }
        catch (JsonException)
        {
            return stringValue;
        }
    }

    [HttpGet("{name}/picture")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetAccountPicture(string name)
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

        if (account.Profile?.Picture is null) return NotFound();
        
        return Redirect($"{configuration["FileUrl"]}/{account.Profile.Picture.Id}");
    }
    
    [HttpGet("{name}/background")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetAccountBackground(string name)
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

        if (account.Profile?.Background is null) return NotFound();
        
        return Redirect($"{configuration["FileUrl"]}/{account.Profile.Background.Id}");
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

    [HttpGet("{name}/board")]
    [ProducesResponseType<List<SnAccountBoardItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SnAccountBoardItem>>> GetAccountBoard(string name)
    {
        var account = await accountService.LookupAccount(name);
        if (account is null)
            return NotFound(ApiError.NotFound(name, traceId: HttpContext.TraceIdentifier));

        return Ok(await boardService.GetBoardAsync(account.Id));
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
