using System.Collections.Concurrent;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

public class MeetExpirationScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<MeetExpirationScheduler> logger
)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

    public void Schedule(Guid meetId, Instant expiresAt)
    {
        Cancel(meetId);

        var cts = new CancellationTokenSource();
        if (!_timers.TryAdd(meetId, cts))
        {
            cts.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = expiresAt - SystemClock.Instance.GetCurrentInstant();
                if (delay > Duration.Zero)
                    await Task.Delay(delay.ToTimeSpan(), cts.Token);

                using var scope = scopeFactory.CreateScope();
                var meetService = scope.ServiceProvider.GetRequiredService<MeetService>();
                await meetService.TryExpireMeetAsync(meetId, CancellationToken.None);
            }
            catch (TaskCanceledException)
            {
                // Expected when the meet reaches a terminal state before expiration.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to expire meet {MeetId}", meetId);
            }
            finally
            {
                if (_timers.TryGetValue(meetId, out var existing) && existing == cts)
                    _timers.TryRemove(meetId, out _);
                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    public void Cancel(Guid meetId)
    {
        if (!_timers.TryRemove(meetId, out var cts)) return;
        cts.Cancel();
        cts.Dispose();
    }
}

public class MeetLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    MeetExpirationScheduler scheduler,
    ILogger<MeetLifecycleHostedService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var meetService = scope.ServiceProvider.GetRequiredService<MeetService>();
        var now = SystemClock.Instance.GetCurrentInstant();

        var activeMeets = await db.Meets
            .Where(m => m.Status == MeetStatus.Active)
            .Select(m => new { m.Id, m.ExpiresAt })
            .ToListAsync(cancellationToken);

        logger.LogInformation("Restoring {Count} active meet expiration schedules.", activeMeets.Count);

        foreach (var meet in activeMeets)
        {
            if (meet.ExpiresAt <= now)
            {
                await meetService.TryExpireMeetAsync(meet.Id, cancellationToken);
                continue;
            }

            scheduler.Schedule(meet.Id, meet.ExpiresAt);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
