using DysonNetwork.Padlock.Auth.OpenId;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    AccountService accounts,
    IEnumerable<OidcService> oidcServices,
    RemoteSubscriptionService remoteSubscription,
    ILogger<AccountServiceGrpc> logger
) : DyAccountService.DyAccountServiceBase
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

        await PopulatePerkSubscriptionAsync(account, context.CancellationToken);
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

        await PopulatePerkSubscriptionAsync(account, context.CancellationToken);
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
        await PopulatePerkSubscriptionsAsync(accounts, context.CancellationToken);

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
        await PopulatePerkSubscriptionsAsync(accounts, context.CancellationToken);

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
        await PopulatePerkSubscriptionsAsync(accounts, context.CancellationToken);

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
        await PopulatePerkSubscriptionsAsync(accounts, context.CancellationToken);

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
        await PopulatePerkSubscriptionsAsync(accounts, context.CancellationToken);

        var response = new DyListAccountsResponse
        {
            TotalSize = totalCount,
            NextPageToken = accounts.Count == pageSize ? (page + 1).ToString() : string.Empty
        };
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    private async Task PopulatePerkSubscriptionAsync(SnAccount account, CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is null)
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
                return;
            }

            var perk = SnWalletSubscription.FromProtoValue(subscription).ToReference();
            account.PerkSubscription = perk;
            account.PerkLevel = perk.PerkLevel;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to populate perk subscription for account {AccountId}", account.Id);
            account.PerkSubscription = null;
            account.PerkLevel = 0;
        }
    }

    private async Task PopulatePerkSubscriptionsAsync(List<SnAccount> accounts, CancellationToken cancellationToken)
    {
        if (accounts.Count == 0) return;

        try
        {
            var subscriptions = await remoteSubscription.GetPerkSubscriptions(accounts.Select(a => a.Id).ToList());
            var subscriptionMap = subscriptions
                .ToDictionary(
                    s => Guid.Parse(s.AccountId),
                    s => SnWalletSubscription.FromProtoValue(s).ToReference()
                );

            foreach (var account in accounts)
            {
                if (subscriptionMap.TryGetValue(account.Id, out var perk))
                {
                    account.PerkSubscription = perk;
                    account.PerkLevel = perk.PerkLevel;
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
            logger.LogWarning(ex, "Failed to populate perk subscriptions for {Count} accounts", accounts.Count);
            foreach (var account in accounts)
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
            }
        }
    }

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

    public override async Task<DyListContactsResponse> GetContactsByProvider(
        DyGetContactsByProviderRequest request,
        ServerCallContext context)
    {
        // This contract is retained for compatibility. Account contacts do not have a "provider" field.
        // Return an empty list until the proto adds a provider-aware contact model.
        await Task.CompletedTask;
        return new DyListContactsResponse();
    }

    public override async Task<DyListContactsResponse> GetContactsByAccount(
        DyGetContactsByAccountRequest request,
        ServerCallContext context)
    {
        var list = await ListContacts(
            new DyListContactsRequest
            {
                AccountId = request.AccountId,
                Type = DyAccountContactType.Unspecified,
                VerifiedOnly = false
            },
            context);

        return list;
    }

    public override async Task<DyListAuthFactorsResponse> ListAuthFactors(
        DyListAuthFactorsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = db.AccountAuthFactors
            .AsNoTracking()
            .Where(f => f.AccountId == accountId);

        if (request.ActiveOnly)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            query = query.Where(f => f.EnabledAt != null && (f.ExpiredAt == null || f.ExpiredAt > now));
        }

        var factors = await query.ToListAsync(context.CancellationToken);
        var response = new DyListAuthFactorsResponse();
        response.Factors.AddRange(factors.Select(ToProtoAuthFactor));
        return response;
    }

    public override async Task<DyAccountAuthFactor> ResetPasswordFactor(
        DyResetPasswordFactorRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "New password is required"));

        var accountExists = await db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == accountId, context.CancellationToken);
        if (!accountExists)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.AccountId} not found"));

        var factor = await accounts.ResetPasswordFactor(accountId, request.NewPassword);
        return ToProtoAuthFactor(factor);
    }

    public override async Task<DyListConnectionsResponse> ListConnections(
        DyListConnectionsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = db.AccountConnections
            .AsNoTracking()
            .Where(c => c.AccountId == accountId);

        if (!string.IsNullOrWhiteSpace(request.Provider))
            query = query.Where(c => c.Provider == request.Provider);

        var connections = await query.ToListAsync(context.CancellationToken);
        var response = new DyListConnectionsResponse();
        response.Connections.AddRange(connections.Select(ToProtoConnection));
        return response;
    }

    public override async Task<DyGetValidAccessTokenResponse> GetValidAccessToken(
        DyGetValidAccessTokenRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ConnectionId, out var connectionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid connection ID format"));

        var connection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, context.CancellationToken);

        if (connection is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Connection not found"));

        var refreshToken = !string.IsNullOrWhiteSpace(request.RefreshToken)
            ? request.RefreshToken
            : connection.RefreshToken;

        var currentAccessToken = !string.IsNullOrWhiteSpace(request.CurrentAccessToken)
            ? request.CurrentAccessToken
            : connection.AccessToken;

        var accessToken = currentAccessToken;
        var provider = oidcServices.FirstOrDefault(s =>
            string.Equals(s.ProviderName, connection.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is not null && !string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                var refreshed = await provider.GetValidAccessTokenAsync(refreshToken, currentAccessToken);
                if (!string.IsNullOrWhiteSpace(refreshed))
                {
                    accessToken = refreshed;
                }
            }
            catch (InvalidOperationException)
            {
                // Provider does not support refresh flow; fallback to existing token.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to refresh access token for provider {Provider} and connection {ConnectionId}",
                    connection.Provider,
                    connection.Id);
            }
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "No valid access token available"));

        connection.AccessToken = accessToken;
        connection.LastUsedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync(context.CancellationToken);

        return new DyGetValidAccessTokenResponse
        {
            AccessToken = accessToken
        };
    }

    private static DyAccountAuthFactor ToProtoAuthFactor(SnAccountAuthFactor factor)
    {
        var proto = new DyAccountAuthFactor
        {
            Id = factor.Id.ToString(),
            Type = factor.Type switch
            {
                AccountAuthFactorType.Password => DyAccountAuthFactorType.DyPassword,
                AccountAuthFactorType.EmailCode => DyAccountAuthFactorType.DyEmailCode,
                AccountAuthFactorType.InAppCode => DyAccountAuthFactorType.DyInAppCode,
                AccountAuthFactorType.TimedCode => DyAccountAuthFactorType.DyTimedCode,
                AccountAuthFactorType.PinCode => DyAccountAuthFactorType.DyPinCode,
                _ => DyAccountAuthFactorType.DyAuthFactorTypeUnspecified
            },
            Trustworthy = factor.Trustworthy,
            EnabledAt = factor.EnabledAt?.ToTimestamp(),
            ExpiredAt = factor.ExpiredAt?.ToTimestamp(),
            AccountId = factor.AccountId.ToString(),
            CreatedAt = factor.CreatedAt.ToTimestamp(),
            UpdatedAt = factor.UpdatedAt.ToTimestamp()
        };

        if (factor.Config is not null)
        {
            proto.Config.Add(InfraObjectCoder.ConvertToValueMap(factor.Config));
        }

        if (factor.CreatedResponse is not null)
        {
            proto.CreatedResponse.Add(InfraObjectCoder.ConvertToValueMap(factor.CreatedResponse));
        }

        return proto;
    }

    private static DyAccountConnection ToProtoConnection(SnAccountConnection connection)
    {
        var proto = new DyAccountConnection
        {
            Id = connection.Id.ToString(),
            Provider = connection.Provider,
            ProvidedIdentifier = connection.ProvidedIdentifier,
            AccessToken = connection.AccessToken,
            RefreshToken = connection.RefreshToken,
            LastUsedAt = connection.LastUsedAt?.ToTimestamp(),
            AccountId = connection.AccountId.ToString(),
            CreatedAt = connection.CreatedAt.ToTimestamp(),
            UpdatedAt = connection.UpdatedAt.ToTimestamp()
        };

        if (connection.Meta is not null)
            proto.Meta.Add(InfraObjectCoder.ConvertToValueMap(connection.Meta));

        return proto;
    }
}
