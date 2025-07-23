using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    RelationshipService relationships,
    SubscriptionService subscriptions,
    IClock clock,
    ILogger<AccountServiceGrpc> logger
)
    : Shared.Proto.AccountService.AccountServiceBase
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<AccountServiceGrpc>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<Shared.Proto.Account> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        var perk = await subscriptions.GetPerkSubscriptionAsync(account.Id);
        account.PerkSubscription = perk?.ToReference();

        return account.ToProtoValue();
    }

    public override async Task<GetAccountBatchResponse> GetAccountBatch(GetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var accountId) ? accountId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Include(a => a.Profile)
            .ToListAsync();
        
        var perks = await subscriptions.GetPerkSubscriptionsAsync(
            accounts.Select(x => x.Id).ToList()
        );
        foreach (var account in accounts)
            if (perks.TryGetValue(account.Id, out var perk))
                account.PerkSubscription = perk?.ToReference();

        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<GetAccountBatchResponse> LookupAccountBatch(LookupAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountNames = request.Names.ToList();
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => accountNames.Contains(a.Name))
            .Include(a => a.Profile)
            .ToListAsync();
        
        var perks = await subscriptions.GetPerkSubscriptionsAsync(
            accounts.Select(x => x.Id).ToList()
        );
        foreach (var account in accounts)
            if (perks.TryGetValue(account.Id, out var perk))
                account.PerkSubscription = perk?.ToReference();
        
        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<ListAccountsResponse> ListAccounts(ListAccountsRequest request,
        ServerCallContext context)
    {
        var query = _db.Accounts.AsNoTracking();

        // Apply filters if provided
        if (!string.IsNullOrEmpty(request.Filter))
        {
            // Implement filtering logic based on request.Filter
            // This is a simplified example
            query = query.Where(a => a.Name.Contains(request.Filter) || a.Nick.Contains(request.Filter));
        }

        // Apply ordering
        query = request.OrderBy switch
        {
            "name" => query.OrderBy(a => a.Name),
            "name_desc" => query.OrderByDescending(a => a.Name),
            _ => query.OrderBy(a => a.Id)
        };

        // Get total count for pagination
        var totalCount = await query.CountAsync();

        // Apply pagination
        var accounts = await query
            .Skip(request.PageSize * (request.PageToken != null ? int.Parse(request.PageToken) : 0))
            .Take(request.PageSize)
            .Include(a => a.Profile)
            .ToListAsync();

        var perks = await subscriptions.GetPerkSubscriptionsAsync(
            accounts.Select(x => x.Id).ToList()
        );
        foreach (var account in accounts)
            if (perks.TryGetValue(account.Id, out var perk))
                account.PerkSubscription = perk?.ToReference();

        var response = new ListAccountsResponse
        {
            TotalSize = totalCount,
            NextPageToken = (accounts.Count == request.PageSize)
                ? ((request.PageToken != null ? int.Parse(request.PageToken) : 0) + 1).ToString()
                : ""
        };

        response.Accounts.AddRange(accounts.Select(x => x.ToProtoValue()));
        return response;
    }

    public override async Task<ListRelationshipSimpleResponse> ListFriends(
        ListRelationshipSimpleRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var relationship = await relationships.ListAccountFriends(accountId);
        var resp = new ListRelationshipSimpleResponse();
        resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
        return resp;
    }

    public override async Task<ListRelationshipSimpleResponse> ListBlocked(
        ListRelationshipSimpleRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var relationship = await relationships.ListAccountBlocked(accountId);
        var resp = new ListRelationshipSimpleResponse();
        resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
        return resp;
    }

    public override async Task<GetRelationshipResponse> GetRelationship(GetRelationshipRequest request,
        ServerCallContext context)
    {
        var relationship = await relationships.GetRelationship(
            Guid.Parse(request.AccountId),
            Guid.Parse(request.RelatedId),
            status: (RelationshipStatus?)request.Status
        );
        return new GetRelationshipResponse
        {
            Relationship = relationship?.ToProtoValue()
        };
    }

    public override async Task<BoolValue> HasRelationship(GetRelationshipRequest request, ServerCallContext context)
    {
        var hasRelationship = false;
        if (!request.HasStatus)
            hasRelationship = await relationships.HasExistingRelationship(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId)
            );
        else
            hasRelationship = await relationships.HasRelationshipWithStatus(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId),
                (RelationshipStatus)request.Status
            );
        return new BoolValue { Value = hasRelationship };
    }
}