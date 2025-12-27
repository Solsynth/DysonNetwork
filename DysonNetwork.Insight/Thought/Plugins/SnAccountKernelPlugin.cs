using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.Thought.Plugins;

public class SnAccountKernelPlugin(
    AccountService.AccountServiceClient accountClient
)
{
    [KernelFunction("get_account")]
    public async Task<SnAccount?> GetAccount(string userId)
    {
        var request = new GetAccountRequest { Id = userId };
        var response = await accountClient.GetAccountAsync(request);
        if (response is null) return null;
        return SnAccount.FromProtoValue(response);
    }

    [KernelFunction("get_account_by_name")]
    public async Task<SnAccount?> GetAccountByName(string username)
    {
        var request = new LookupAccountBatchRequest();
        request.Names.Add(username);
        var response = await accountClient.LookupAccountBatchAsync(request);
        return response.Accounts.Count == 0 ? null : SnAccount.FromProtoValue(response.Accounts[0]);
    }
}