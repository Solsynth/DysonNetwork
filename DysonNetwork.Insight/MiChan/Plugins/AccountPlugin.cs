using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Insight.MiChan.Plugins;

public class AccountPlugin(SolarNetworkApiClient apiClient, ILogger<AccountPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [AgentTool("get_account_info", Description = "Get information about a user account. Returns JSON string with account details.")]
    public async Task<string> GetAccountInfo(
        [AgentToolParameter("The ID or username of the account")] string accountIdOrUsername
    )
    {
        try
        {
            SnAccount? account;
            
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

    [AgentTool("search_accounts", Description = "Search for user accounts. Returns JSON array of matching accounts.")]
    public async Task<string> SearchAccounts(
        [AgentToolParameter("Search query")] string query,
        [AgentToolParameter("Maximum number of results")] int limit = 20
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

    [AgentTool("get_bot_profile", Description = "Get the bot's own profile information. Returns JSON with account details.")]
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
