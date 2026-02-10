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
                $"/accounts/search?q={Uri.EscapeDataString(query)}&take={limit}"
            );
            
            return accounts ?? new List<SnAccount>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search accounts with query: {Query}", query);
            return null;
        }
    }

    [KernelFunction("follow_account")]
    [Description("Follow another user account.")]
    public async Task<object> FollowAccount(
        [Description("The ID of the account to follow")] string accountId
    )
    {
        try
        {
            await apiClient.PostAsync("pass", $"/accounts/{accountId}/follow", new { });
            
            logger.LogInformation("Followed account {AccountId}", accountId);
            return new { success = true, message = "Account followed successfully" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to follow account {AccountId}", accountId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("unfollow_account")]
    [Description("Unfollow another user account.")]
    public async Task<object> UnfollowAccount(
        [Description("The ID of the account to unfollow")] string accountId
    )
    {
        try
        {
            await apiClient.PostAsync("pass", $"/accounts/{accountId}/unfollow", new { });
            
            logger.LogInformation("Unfollowed account {AccountId}", accountId);
            return new { success = true, message = "Account unfollowed successfully" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unfollow account {AccountId}", accountId);
            return new { success = false, error = ex.Message };
        }
    }

    [KernelFunction("get_followers")]
    [Description("Get the list of followers for an account.")]
    public async Task<List<SnAccount>?> GetFollowers(
        [Description("The ID of the account")] string accountId,
        [Description("Maximum number of results")] int limit = 50
    )
    {
        try
        {
            var followers = await apiClient.GetAsync<List<SnAccount>>(
                "pass", 
                $"/accounts/{accountId}/followers?take={limit}"
            );
            
            return followers ?? new List<SnAccount>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get followers for account {AccountId}", accountId);
            return null;
        }
    }

    [KernelFunction("get_following")]
    [Description("Get the list of accounts that a user is following.")]
    public async Task<List<SnAccount>?> GetFollowing(
        [Description("The ID of the account")] string accountId,
        [Description("Maximum number of results")] int limit = 50
    )
    {
        try
        {
            var following = await apiClient.GetAsync<List<SnAccount>>(
                "pass", 
                $"/accounts/{accountId}/following?take={limit}"
            );
            
            return following ?? new List<SnAccount>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get following for account {AccountId}", accountId);
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
