using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Live;

public class LiveStreamIngressCleanupJob : IJob
{
    private readonly AppDatabase _db;
    private readonly LiveKitLivestreamService _liveKitService;
    private readonly ILogger<LiveStreamIngressCleanupJob> _logger;

    public LiveStreamIngressCleanupJob(
        AppDatabase db,
        LiveKitLivestreamService liveKitService,
        ILogger<LiveStreamIngressCleanupJob> logger)
    {
        _db = db;
        _liveKitService = liveKitService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var threshold = now - Duration.FromMinutes(5);

        var staleStreams = await _db.LiveStreams
            .Where(ls => ls.Status == LiveStreamStatus.Active
                      && ls.IngressId != null
                      && ls.StartedAt != null
                      && ls.StartedAt < threshold)
            .ToListAsync();

        if (staleStreams.Count == 0)
        {
            _logger.LogDebug("No stale livestreams found");
            return;
        }

        _logger.LogInformation("Found {Count} stale livestreams with unused ingress", staleStreams.Count);

        foreach (var stream in staleStreams)
        {
            try
            {
                _logger.LogInformation(
                    "Marking livestream {Id} as ended - ingress created but not used within 5 minutes",
                    stream.Id);

                stream.Status = LiveStreamStatus.Ended;
                stream.EndedAt = now;

                if (!string.IsNullOrEmpty(stream.IngressId))
                {
                    try
                    {
                        await _liveKitService.DeleteIngressAsync(stream.IngressId);
                        _logger.LogDebug("Deleted unused ingress {IngressId} for livestream {Id}", 
                            stream.IngressId, stream.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete ingress {IngressId} for livestream {Id}", 
                            stream.IngressId, stream.Id);
                    }
                }

                stream.IngressId = null;
                stream.IngressStreamKey = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark livestream {Id} as ended", stream.Id);
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Cleaned up {Count} stale livestreams", staleStreams.Count);
    }
}
