using NodaTime;

namespace DysonNetwork.Insight.Thought.Voice;

public class ThinkingVoiceCleanupHostedService(
    IServiceProvider serviceProvider,
    ILogger<ThinkingVoiceCleanupHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);

        var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var voiceService = scope.ServiceProvider.GetRequiredService<ThinkingVoiceService>();
                    var deleted = await voiceService.CleanupExpiredVoiceClipsAsync(stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Cleaned up {Count} expired thought voice clips.", deleted);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup expired thought voice clips");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }
}
