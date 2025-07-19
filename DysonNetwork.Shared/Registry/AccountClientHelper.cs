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
    
    public async Task<List<Account>> GetAccountBatch(List<Guid> ids)
    {
        var request = new GetAccountBatchRequest();
        request.Id.AddRange(ids.Select(id => id.ToString()));
        var response = await accounts.GetAccountBatchAsync(request);
        return response.Accounts.ToList();
    }
}