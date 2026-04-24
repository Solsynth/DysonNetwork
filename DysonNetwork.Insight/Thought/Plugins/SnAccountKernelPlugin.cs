using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.Thought.Plugins;

public class SnAccountKernelPlugin(
    DyAccountService.DyAccountServiceClient accountClient
)
{
    [AgentTool("get_account")]
    public async Task<string> GetAccount(string userId)
    {
        var request = new DyGetAccountRequest { Id = userId };
        var response = await accountClient.GetAccountAsync(request);
        if (response is null) return KernelPluginUtils.ToJson(new { error = "Account not found" });
        return KernelPluginUtils.ToJson(SnAccount.FromProtoValue(response));
    }

    [AgentTool("get_account_by_name")]
    public async Task<string> GetAccountByName(string username)
    {
        var request = new DyLookupAccountBatchRequest();
        request.Names.Add(username);
        var response = await accountClient.LookupAccountBatchAsync(request);
        return response.Accounts.Count == 0 
            ? KernelPluginUtils.ToJson(new { error = $"Account {username} not found" }) 
            : KernelPluginUtils.ToJson(SnAccount.FromProtoValue(response.Accounts[0]));
    }
}
