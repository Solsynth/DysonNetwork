using System.ComponentModel;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class AccountPlugin(SolarNetworkApiClient apiClient, ILogger<AccountPlugin> logger)
{
    [KernelFunction("get_account_info")]
    [Description("Get information about a user account.")]
    public async Task<SnAccount?> GetAccountInfo(
        [Description("The ID or username of the account")] string accountIdOrUsername
    )
    {
        try
        {
            // Try to parse as Guid first (ID), otherwise treat as username
            if (Guid.TryParse(accountIdOrUsername, out _))
            {
                var account = await apiClient.GetAsync<SnAccount>("pass", $"/accounts/{accountIdOrUsername}");
                return account;
            }
            else
            {
                var accounts = await apiClient.GetAsync<List<SnAccount>>(
                    "pass", 
                    $"/accounts/search?q={Uri.EscapeDataString(accountIdOrUsername)}"
                );
                return accounts?.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get account info for {Account}", accountIdOrUsername);
            return null;
        }
    }

    [KernelFunction("search_accounts")]
    [Description("Search for user accounts.")]
    public async Task<List<SnAccount>?> SearchAccounts(
        [Description("Search query")] string query,
        [Description("Maximum number of results")] int limit = 20
    )
    {
        try
        {
            var accounts = await apiClient.GetAsync<List<SnAccount>>(
                "pass", 
                $"/accounts/search?query={Uri.EscapeDataString(query)}&take={limit}"
            );
            
            return accounts ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search accounts with query: {Query}", query);
            return null;
        }
    }

    [KernelFunction("get_bot_profile")]
    [Description("Get the bot's own profile information.")]
    public async Task<SnAccount?> GetBotProfile()
    {
        try
        {
            var account = await apiClient.GetAsync<SnAccount>("pass", "/accounts/me");
            return account;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get bot profile");
            return null;
        }
    }
}
