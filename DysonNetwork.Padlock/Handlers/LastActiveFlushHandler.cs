using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Handlers;

public class LastActiveInfo
{
    public required SnAccount Account { get; set; }
    public required SnAuthSession Session { get; set; }
    public required Instant SeenAt { get; set; }
}

public class LastActiveFlushHandler(
    AppDatabase db,
    DyProfileService.DyProfileServiceClient profiles,
    ILogger<LastActiveFlushHandler> logger
) : IFlushHandler<LastActiveInfo>
{
    public async Task FlushAsync(IReadOnlyList<LastActiveInfo> items)
    {
        if (items.Count == 0) return;

        var distinctItems = items
            .GroupBy(x => (x.Session.Id, x.Account.Id))
            .Select(g => g.OrderByDescending(x => x.SeenAt).First())
            .ToList();

        var sessionIds = distinctItems.Select(x => x.Session.Id).Distinct().ToList();
        var maxSeenAt = distinctItems.Max(x => x.SeenAt);

        var updatedSessions = await db.AuthSessions
            .Where(s => sessionIds.Contains(s.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastGrantedAt, maxSeenAt));

        logger.LogDebug("Flushed last-active for {SessionCount} sessions", updatedSessions);

        var accountLastSeen = distinctItems
            .GroupBy(x => x.Account.Id)
            .ToDictionary(g => g.Key, g => g.Max(x => x.SeenAt));

        foreach (var pair in accountLastSeen)
        {
            try
            {
                await profiles.UpdateProfileAsync(new DyUpdateProfileRequest
                {
                    AccountId = pair.Key.ToString(),
                    Profile = new DyAccountProfile
                    {
                        LastSeenAt = pair.Value.ToTimestamp()
                    },
                    UpdateMask = new FieldMask
                    {
                        Paths = { "last_seen_at" }
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to flush profile last_seen_at for account {AccountId}", pair.Key);
            }
        }
    }
}

public class LastActiveFlushBackgroundService(
    FlushBufferService flushBufferService,
    IServiceScopeFactory scopeFactory,
    ILogger<LastActiveFlushBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasNext = await timer.WaitForNextTickAsync(stoppingToken);
                if (!hasNext) break;

                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<LastActiveFlushHandler>();
                await flushBufferService.FlushAsync(handler);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during last-active background flush");
            }
        }
    }
}
