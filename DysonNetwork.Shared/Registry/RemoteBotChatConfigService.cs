using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

/// <summary>
/// Remote service to fetch bot chat configuration from the Develop service.
/// Uses HTTP since we can't easily modify proto files.
/// </summary>
public class RemoteBotChatConfigService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RemoteBotChatConfigService> logger
)
{
    private readonly string _developBaseUrl = configuration["Services:Develop:BaseUrl"] 
        ?? "https://_grpc.develop";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<SnBotChatConfig?> GetBotChatConfigAsync(Guid botId)
    {
        try
        {
            var url = $"{_developBaseUrl}/api/bots/public/{botId}/chat";
            var response = await httpClient.GetAsync(url);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
                
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SnBotChatConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get bot chat config for {BotId}", botId);
            return null;
        }
    }

    public async Task<SnDeveloper?> GetBotDeveloperAsync(Guid botId)
    {
        try
        {
            var url = $"{_developBaseUrl}/api/bots/public/{botId}/developer";
            var response = await httpClient.GetAsync(url);
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
                
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SnDeveloper>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get bot developer for {BotId}", botId);
            return null;
        }
    }
}
