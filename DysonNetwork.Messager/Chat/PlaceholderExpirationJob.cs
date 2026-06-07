using Quartz;

namespace DysonNetwork.Messager.Chat;

public class PlaceholderExpirationJob(
    ChatService chatService,
    ILogger<PlaceholderExpirationJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var expired = await chatService.ExpireStalePlaceholdersAsync();
        if (expired > 0)
            logger.LogInformation("Placeholder expiration cleaned up {Count} stale placeholders.", expired);
    }
}
