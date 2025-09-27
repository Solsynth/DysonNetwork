using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class AccountClientHelper(AccountService.AccountServiceClient accounts)
{
    public async Task<Account> GetAccount(Guid id)
    {
        var request = new GetAccountRequest { Id = id.ToString() };
        var response = await accounts.GetAccountAsync(request);
        return response;
    }
    
    public async Task<Account> GetBotAccount(Guid automatedId)
    {
        var request = new GetBotAccountRequest { AutomatedId = automatedId.ToString() };
        var response = await accounts.GetBotAccountAsync(request);
        return response;
    }

    public async Task<List<Account>> GetAccountBatch(List<Guid> ids)
    {
        var request = new GetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await accounts.GetAccountBatchAsync(request);
        return response.Accounts.ToList();
    }
    
    public async Task<List<Account>> GetBotAccountBatch(List<Guid> automatedIds)
    {
        var request = new GetBotAccountBatchRequest();
        request.AutomatedId.AddRange(automatedIds.Select(id => id.ToString()));
        var response = await accounts.GetBotAccountBatchAsync(request);
        return response.Accounts.ToList();
    }

    public async Task<Dictionary<Guid, SnAccountStatus>> GetAccountStatusBatch(List<Guid> ids)
    {
        var request = new GetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await accounts.GetAccountStatusBatchAsync(request);
        return response.Statuses
            .Select(SnAccountStatus.FromProtoValue)
            .ToDictionary(s => s.AccountId, s => s);
    }
}