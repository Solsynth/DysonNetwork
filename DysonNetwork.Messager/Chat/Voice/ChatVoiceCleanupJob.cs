using Quartz;

namespace DysonNetwork.Messager.Chat.Voice;

public class ChatVoiceCleanupJob(
    ChatVoiceService voiceService,
    ILogger<ChatVoiceCleanupJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var removed = await voiceService.CleanupExpiredVoiceClipsAsync(context.CancellationToken);
        if (removed > 0)
            logger.LogInformation("Voice cleanup removed {Count} expired clips.", removed);
    }
}
