using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class AccountPlugin(SolarNetworkApiClient apiClient, ILogger<AccountPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [KernelFunction("get_account_info")]
    [Description("Get information about a user account. Returns JSON string with account details.")]
    public async Task<string> GetAccountInfo(
        [Description("The ID or username of the account")] string accountIdOrUsername
    )
    {
        try
        {
            SnAccount? account;
            
            // Try to parse as Guid first (ID), otherwise treat as username
            if (Guid.TryParse(accountIdOrUsername, out _))
            {
                account = await apiClient.GetAsync<SnAccount>("passport", $"/accounts/{accountIdOrUsername}");
            }
            else
            {
                var accounts = await apiClient.GetAsync<List<SnAccount>>(
                    "passport", 
                    $"/accounts/search?q={Uri.EscapeDataString(accountIdOrUsername)}"
                );
                account = accounts?.FirstOrDefault();
            }

            if (account == null)
            {
                return JsonSerializer.Serialize(new { error = "Account not found" }, JsonOptions);
            }

            return JsonSerializer.Serialize(account, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get account info for {Account}", accountIdOrUsername);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("search_accounts")]
    [Description("Search for user accounts. Returns JSON array of matching accounts.")]
    public async Task<string> SearchAccounts(
        [Description("Search query")] string query,
        [Description("Maximum number of results")] int limit = 20
    )
    {
        try
        {
            var accounts = await apiClient.GetAsync<List<SnAccount>>(
                "passport", 
                $"/accounts/search?q={Uri.EscapeDataString(query)}&take={limit}"
            );
            
            var result = accounts ?? new List<SnAccount>();
            return JsonSerializer.Serialize(new { count = result.Count, accounts = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search accounts with query: {Query}", query);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("follow_account")]
    [Description("Follow another user account.")]
    public async Task<string> FollowAccount(
        [Description("The ID of the account to follow")] string accountId
    )
    {
        try
        {
            await apiClient.PostAsync("passport", $"/accounts/{accountId}/follow", new { });
            
            logger.LogInformation("Followed account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { success = true, message = "Account followed successfully" }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to follow account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("unfollow_account")]
    [Description("Unfollow another user account.")]
    public async Task<string> UnfollowAccount(
        [Description("The ID of the account to unfollow")] string accountId
    )
    {
        try
        {
            await apiClient.PostAsync("passport", $"/accounts/{accountId}/unfollow", new { });
            
            logger.LogInformation("Unfollowed account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { success = true, message = "Account unfollowed successfully" }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unfollow account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { success = false, error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_followers")]
    [Description("Get the list of followers for an account. Returns JSON array of follower accounts.")]
    public async Task<string> GetFollowers(
        [Description("The ID of the account")] string accountId,
        [Description("Maximum number of results")] int limit = 50
    )
    {
        try
        {
            var followers = await apiClient.GetAsync<List<SnAccount>>(
                "passport", 
                $"/accounts/{accountId}/followers?take={limit}"
            );
            
            var result = followers ?? new List<SnAccount>();
            return JsonSerializer.Serialize(new { count = result.Count, followers = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get followers for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_following")]
    [Description("Get the list of accounts that a user is following. Returns JSON array of followed accounts.")]
    public async Task<string> GetFollowing(
        [Description("The ID of the account")] string accountId,
        [Description("Maximum number of results")] int limit = 50
    )
    {
        try
        {
            var following = await apiClient.GetAsync<List<SnAccount>>(
                "passport", 
                $"/accounts/{accountId}/following?take={limit}"
            );
            
            var result = following ?? new List<SnAccount>();
            return JsonSerializer.Serialize(new { count = result.Count, following = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get following for account {AccountId}", accountId);
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }

    [KernelFunction("get_bot_profile")]
    [Description("Get the bot's own profile information. Returns JSON with account details.")]
    public async Task<string> GetBotProfile()
    {
        try
        {
            var account = await apiClient.GetAsync<SnAccount>("passport", "/accounts/me");
            
            if (account == null)
            {
                return JsonSerializer.Serialize(new { error = "Could not retrieve bot profile" }, JsonOptions);
            }

            return JsonSerializer.Serialize(account, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get bot profile");
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }
    }
}
