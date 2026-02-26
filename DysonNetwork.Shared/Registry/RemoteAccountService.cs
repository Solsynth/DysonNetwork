using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteAccountService(DyAccountService.DyAccountServiceClient accounts)
{
    public async Task<DyAccount> GetAccount(Guid id)
    {
        var request = new DyGetAccountRequest { Id = id.ToString() };
        var response = await accounts.GetAccountAsync(request);
        return response;
    }

    public async Task<DyAccount> GetBotAccount(Guid automatedId)
    {
        var request = new DyGetBotAccountRequest { AutomatedId = automatedId.ToString() };
        var response = await accounts.GetBotAccountAsync(request);
        return response;
    }

    public async Task<List<DyAccount>> GetAccountBatch(List<Guid> ids)
    {
        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await accounts.GetAccountBatchAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<List<DyAccount>> SearchAccounts(string query)
    {
        var request = new DySearchAccountRequest { Query = query };
        var response = await accounts.SearchAccountAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<List<DyAccount>> GetBotAccountBatch(List<Guid> automatedIds)
    {
        var request = new DyGetBotAccountBatchRequest();
        request.AutomatedId.AddRange(automatedIds.Select(id => id.ToString()));
        var response = await accounts.GetBotAccountBatchAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<Dictionary<Guid, SnAccountStatus>> GetAccountStatusBatch(List<Guid> ids)
    {
        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await accounts.GetAccountStatusBatchAsync(request);
        return response.Statuses
            .Select(SnAccountStatus.FromProtoValue)
            .ToDictionary(s => s.AccountId, s => s);
    }

    public async Task<DyAccountBadge> GrantBadge(Guid accountId, DyAccountBadge badge)
    {
        var request = new DyGrantBadgeRequest
        {
            AccountId = accountId.ToString(),
            Badge = badge
        };
        var response = await accounts.GrantBadgeAsync(request);
        return response.Badge;
    }

    public async Task<DyAccountBadge> GetBadge(Guid accountId, Guid badgeId)
    {
        var request = new DyGetBadgeRequest
        {
            AccountId = accountId.ToString(),
            BadgeId = badgeId.ToString()
        };
        var response = await accounts.GetBadgeAsync(request);
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

        var response = await accounts.UpdateBadgeAsync(request);
        return response.Badge;
    }
}