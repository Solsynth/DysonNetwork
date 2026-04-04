using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class LocationPinLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<LocationPinLifecycleHostedService> logger
) : IHostedService
{
    private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _offlineCheckInterval = TimeSpan.FromMinutes(1);
    private Timer? _flushTimer;
    private Timer? _offlineCheckTimer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting LocationPin lifecycle service");

        _flushTimer = new Timer(
            async _ => await FlushDirtyPinsAsync(),
            null,
            TimeSpan.Zero,
            _flushInterval
        );

        _offlineCheckTimer = new Timer(
            async _ => await CheckOfflinePinsAsync(),
            null,
            TimeSpan.Zero,
            _offlineCheckInterval
        );

        await Task.CompletedTask;
    }

    private async Task FlushDirtyPinsAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var pinService = scope.ServiceProvider.GetRequiredService<LocationPinService>();
            await pinService.FlushAllDirtyPinsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error flushing dirty location pins");
        }
    }

    private async Task CheckOfflinePinsAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var pinService = scope.ServiceProvider.GetRequiredService<LocationPinService>();
            await pinService.CheckAndExpireOfflinePinsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking offline location pins");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping LocationPin lifecycle service");

        _flushTimer?.Change(Timeout.Infinite, 0);
        _offlineCheckTimer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }
}
