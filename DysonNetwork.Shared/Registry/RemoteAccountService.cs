using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

public class RemoteAccountService(
    DyProfileService.DyProfileServiceClient profiles,
    ILogger<RemoteAccountService> logger
)
{
    public async Task<DyAccount> GetAccount(Guid id)
    {
        var request = new DyGetAccountRequest { Id = id.ToString() };
        var response = await profiles.GetAccountAsync(request);
        return response;
    }

    public async Task<DyAccount> GetBotAccount(Guid automatedId)
    {
        var request = new DyGetBotAccountRequest { AutomatedId = automatedId.ToString() };
        var response = await profiles.GetBotAccountAsync(request);
        return response;
    }

    public async Task<List<DyAccount>> GetAccountBatch(List<Guid> ids)
    {
        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await profiles.GetAccountBatchAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<List<DyAccount>> SearchAccounts(string query)
    {
        var request = new DySearchAccountRequest { Query = query };
        var response = await profiles.SearchAccountAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<List<DyAccount>> GetBotAccountBatch(List<Guid> automatedIds)
    {
        if (automatedIds == null || automatedIds.Count == 0)
            return [];

        var request = new DyGetBotAccountBatchRequest();
        request.AutomatedId.AddRange(automatedIds.Select(id => id.ToString()));
        var response = await profiles.GetBotAccountBatchAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<Dictionary<Guid, SnAccountStatus>> GetAccountStatusBatch(List<Guid> ids)
    {
        if (ids == null || ids.Count == 0) return [];

        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        try
        {
            var response = await profiles.GetAccountStatusBatchAsync(request);
            return response.Statuses
                .Select(SnAccountStatus.FromProtoValue)
                .ToDictionary(s => s.AccountId, s => s);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Unimplemented)
        {
            logger.LogWarning(
                ex,
                "Profile service unavailable while getting account statuses, falling back to offline defaults for {Count} accounts.",
                ids.Count
            );

            return ids.Distinct().ToDictionary(
                id => id,
                id => new SnAccountStatus
                {
                    AccountId = id,
                    Attitude = StatusAttitude.Neutral,
                    IsOnline = false,
                    IsCustomized = false,
                    IsInvisible = false,
                    IsNotDisturb = false
                }
            );
        }
    }

    public async Task<DyAccountBadge> GrantBadge(Guid accountId, DyAccountBadge badge)
    {
        var request = new DyGrantBadgeRequest
        {
            AccountId = accountId.ToString(),
            Badge = badge
        };
        var response = await profiles.GrantBadgeAsync(request);
        return response.Badge;
    }

    public async Task<DyAccountBadge> GetBadge(Guid accountId, Guid badgeId)
    {
        var request = new DyGetBadgeRequest
        {
            AccountId = accountId.ToString(),
            BadgeId = badgeId.ToString()
        };
        var response = await profiles.GetBadgeAsync(request);
        return response.Badge;
    }

    public async Task<DyAccountBadge> UpdateBadge(
        Guid accountId,
        Guid badgeId,
        DyAccountBadge badge,
        Google.Protobuf.WellKnownTypes.FieldMask? updateMask = null
    )
    {
        var request = new DyUpdateBadgeRequest
        {
            AccountId = accountId.ToString(),
            BadgeId = badgeId.ToString(),
            Badge = badge
        };

        if (updateMask != null && updateMask.Paths.Count > 0)
        {
            request.UpdateMask = updateMask;
        }

        var response = await profiles.UpdateBadgeAsync(request);
        return response.Badge;
    }
}
