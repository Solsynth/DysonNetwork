using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Account;

public class AccountServiceGrpc(AppDatabase db) : DyAccountService.DyAccountServiceBase
{
    public override async Task<DyAccount> GetAccount(DyGetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, context.CancellationToken);

        if (account is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        return account.ToProtoValue();
    }

    public override async Task<DyAccount> GetBotAccount(DyGetBotAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AutomatedId, out var automatedId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid automated ID format"));

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AutomatedId == automatedId, context.CancellationToken);

        if (account is null)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Account with automated ID {request.AutomatedId} not found"));

        return account.ToProtoValue();
    }

    public override async Task<DyGetAccountBatchResponse> GetAccountBatch(
        DyGetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var parsedId) ? parsedId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetAccountBatchResponse> GetBotAccountBatch(
        DyGetBotAccountBatchRequest request,
        ServerCallContext context)
    {
        var automatedIds = request.AutomatedId
            .Select(id => Guid.TryParse(id, out var parsedId) ? parsedId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.AutomatedId.HasValue && automatedIds.Contains(a.AutomatedId.Value))
            .ToListAsync(context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetAccountBatchResponse> LookupAccountBatch(
        DyLookupAccountBatchRequest request,
        ServerCallContext context)
    {
        var names = request.Names.ToList();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => names.Contains(a.Name))
            .ToListAsync(context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetAccountBatchResponse> SearchAccount(
        DySearchAccountRequest request,
        ServerCallContext context)
    {
        var query = request.Query.Trim();
        if (string.IsNullOrEmpty(query))
            return new DyGetAccountBatchResponse();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => EF.Functions.ILike(a.Name, $"%{query}%") || EF.Functions.ILike(a.Nick, $"%{query}%"))
            .Take(100)
            .ToListAsync(context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyListAccountsResponse> ListAccounts(DyListAccountsRequest request,
        ServerCallContext context)
    {
        var query = db.Accounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            var filter = request.Filter.Trim();
            query = query.Where(a => EF.Functions.ILike(a.Name, $"%{filter}%") || EF.Functions.ILike(a.Nick, $"%{filter}%"));
        }

        query = request.OrderBy switch
        {
            "name" => query.OrderBy(a => a.Name),
            "name_desc" => query.OrderByDescending(a => a.Name),
            "created_at_desc" => query.OrderByDescending(a => a.CreatedAt),
            _ => query.OrderBy(a => a.Id)
        };

        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);
        var page = int.TryParse(request.PageToken, out var parsedPage) ? Math.Max(0, parsedPage) : 0;
        var totalCount = await query.CountAsync(context.CancellationToken);

        var accounts = await query
            .Skip(pageSize * page)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new DyListAccountsResponse
        {
            TotalSize = totalCount,
            NextPageToken = accounts.Count == pageSize ? (page + 1).ToString() : string.Empty
        };
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyAccountStatus> GetAccountStatus(DyGetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, context.CancellationToken);
        if (account is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        return new DyAccountStatus
        {
            AccountId = account.Id.ToString(),
            IsOnline = false
        };
    }

    public override async Task<DyGetAccountStatusBatchResponse> GetAccountStatusBatch(
        DyGetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var parsedId) ? parsedId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.UpdatedAt })
            .ToListAsync(context.CancellationToken);

        var response = new DyGetAccountStatusBatchResponse();
        response.Statuses.AddRange(accounts.Select(a => new DyAccountStatus
        {
            AccountId = a.Id.ToString(),
            IsOnline = false
        }));
        return response;
    }

    public override Task<DyListRelationshipSimpleResponse> ListFriends(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context) => Task.FromResult(new DyListRelationshipSimpleResponse());

    public override Task<DyListRelationshipSimpleResponse> ListBlocked(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context) => Task.FromResult(new DyListRelationshipSimpleResponse());

    public override Task<DyGetRelationshipResponse> GetRelationship(
        DyGetRelationshipRequest request,
        ServerCallContext context) => Task.FromResult(new DyGetRelationshipResponse());

    public override Task<BoolValue> HasRelationship(
        DyGetRelationshipRequest request,
        ServerCallContext context) => Task.FromResult(new BoolValue { Value = false });

    public override Task<DyAccountProfile> GetProfile(DyGetProfileRequest request, ServerCallContext context)
        => Task.FromResult(new DyAccountProfile());

    public override Task<DyGrantBadgeResponse> GrantBadge(DyGrantBadgeRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Badge ownership remains in Passport"));

    public override Task<DyGetBadgeResponse> GetBadge(DyGetBadgeRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Badge ownership remains in Passport"));

    public override Task<DyUpdateBadgeResponse> UpdateBadge(DyUpdateBadgeRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Badge ownership remains in Passport"));

    public override async Task<DyListContactsResponse> ListContacts(DyListContactsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = db.AccountContacts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId);

        if (request.VerifiedOnly)
            query = query.Where(c => c.VerifiedAt != null);

        if (request.Type != DyAccountContactType.Unspecified)
        {
            var contactType = request.Type switch
            {
                DyAccountContactType.DyEmail => AccountContactType.Email,
                DyAccountContactType.DyPhoneNumber => AccountContactType.PhoneNumber,
                DyAccountContactType.DyAddress => AccountContactType.Address,
                _ => AccountContactType.Email
            };
            query = query.Where(c => c.Type == contactType);
        }

        var contacts = await query.ToListAsync(context.CancellationToken);
        var response = new DyListContactsResponse();
        response.Contacts.AddRange(contacts.Select(c => c.ToProtoValue()));
        return response;
    }
}
