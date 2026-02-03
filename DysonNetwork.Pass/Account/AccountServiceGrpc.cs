using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    AccountEventService accountEvents,
    RelationshipService relationships,
    RemoteSubscriptionService remoteSubscription,
    ILogger<AccountServiceGrpc> logger
)
    : Shared.Proto.AccountService.AccountServiceBase
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));

    private readonly ILogger<AccountServiceGrpc>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<Shared.Proto.Account> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .Include(a => a.Contacts.Where(c => c.IsPublic))
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account == null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        // Populate PerkSubscription from Wallet service via gRPC
        await PopulatePerkSubscriptionAsync(account);

        return account.ToProtoValue();
    }

    public override async Task<Shared.Proto.Account> GetBotAccount(GetBotAccountRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AutomatedId, out var automatedId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid automated ID format"));

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.AutomatedId == automatedId);

        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound,
                $"Account with automated ID {request.AutomatedId} not found"));

        // Populate PerkSubscription from Wallet service via gRPC
        await PopulatePerkSubscriptionAsync(account);

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

        // Populate PerkSubscriptions from Wallet service via gRPC
        await PopulatePerkSubscriptionsAsync(accounts);

        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }


    public override async Task<GetAccountBatchResponse> GetBotAccountBatch(GetBotAccountBatchRequest request,
        ServerCallContext context)
    {
        var automatedIds = request.AutomatedId
            .Select(id => Guid.TryParse(id, out var automatedId) ? automatedId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.AutomatedId != null && automatedIds.Contains(a.AutomatedId.Value))
            .Include(a => a.Profile)
            .ToListAsync();

        // Populate PerkSubscriptions from Wallet service via gRPC
        await PopulatePerkSubscriptionsAsync(accounts);

        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<AccountStatus> GetAccountStatus(GetAccountRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.Id);
        var status = await accountEvents.GetStatus(accountId);
        return status.ToProtoValue();
    }

    public override async Task<GetAccountStatusBatchResponse> GetAccountStatusBatch(GetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var accountId) ? accountId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
        var statuses = await accountEvents.GetStatuses(accountIds);
        var response = new GetAccountStatusBatchResponse();
        response.Statuses.AddRange(statuses.Select(s => s.Value.ToProtoValue()));
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

        // Populate PerkSubscriptions from Wallet service via gRPC
        await PopulatePerkSubscriptionsAsync(accounts);

        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<GetAccountBatchResponse> SearchAccount(SearchAccountRequest request,
        ServerCallContext context)
    {
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.Name, $"%{request.Query}%"))
            .Include(a => a.Profile)
            .ToListAsync();

        // Populate PerkSubscriptions from Wallet service via gRPC
        await PopulatePerkSubscriptionsAsync(accounts);

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

        // Populate PerkSubscriptions from Wallet service via gRPC
        await PopulatePerkSubscriptionsAsync(accounts);

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
        var resp = new ListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var relationship = await relationships.ListAccountFriends(accountId);
                resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
                return resp;
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.RelatedId:
                var relatedId = Guid.Parse(request.RelatedId);
                var relatedRelationship = await relationships.ListAccountFriends(relatedId, true);
                resp.AccountsId.AddRange(relatedRelationship.Select(x => x.ToString()));
                return resp;
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"The relationship identifier must be provided."));
        }
    }

    public override async Task<ListRelationshipSimpleResponse> ListBlocked(
        ListRelationshipSimpleRequest request, ServerCallContext context)
    {
        var resp = new ListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var relationship = await relationships.ListAccountBlocked(accountId);
                resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
                return resp;
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.RelatedId:
                var relatedId = Guid.Parse(request.RelatedId);
                var relatedRelationship = await relationships.ListAccountBlocked(relatedId, true);
                resp.AccountsId.AddRange(relatedRelationship.Select(x => x.ToString()));
                return resp;
            case ListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"The relationship identifier must be provided."));
        }
    }

    public override async Task<GetRelationshipResponse> GetRelationship(GetRelationshipRequest request,
        ServerCallContext context)
    {
        var relationship = await relationships.GetRelationship(
            Guid.Parse(request.AccountId),
            Guid.Parse(request.RelatedId),
            status: (Shared.Models.RelationshipStatus?)request.Status
        );
        return new GetRelationshipResponse
        {
            Relationship = relationship?.ToProtoValue()
        };
    }

    public override async Task<BoolValue> HasRelationship(GetRelationshipRequest request, ServerCallContext context)
    {
        bool hasRelationship;
        if (!request.HasStatus)
            hasRelationship = await relationships.HasExistingRelationship(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId)
            );
        else
            hasRelationship = await relationships.HasRelationshipWithStatus(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId),
                (Shared.Models.RelationshipStatus)request.Status
            );
        return new BoolValue { Value = hasRelationship };
    }

    /// <summary>
    /// Populates the PerkSubscription property for a single account by calling the Wallet service via gRPC.
    /// </summary>
    private async Task PopulatePerkSubscriptionAsync(SnAccount account)
    {
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription != null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate PerkSubscription for account {AccountId} in gRPC service", account.Id);
        }
    }

    /// <summary>
    /// Populates the PerkSubscription property for multiple accounts by calling the Wallet service via gRPC.
    /// </summary>
    private async Task PopulatePerkSubscriptionsAsync(List<SnAccount> accounts)
    {
        if (accounts.Count == 0) return;

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
            _logger.LogError(ex, "Failed to populate PerkSubscriptions for {Count} accounts in gRPC service", accounts.Count);
        }
    }
}
