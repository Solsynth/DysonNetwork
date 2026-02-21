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

        _logger.LogInformation("Found {Count} livestreams with ingress older than 5 minutes", staleStreams.Count);

        var cleanedCount = 0;

        foreach (var stream in staleStreams)
        {
            try
            {
                var roomDetails = await _liveKitService.GetRoomDetailsAsync(stream.RoomName);
                var hasParticipants = roomDetails != null && roomDetails.ParticipantCount > 0;

                if (hasParticipants)
                {
                    _logger.LogDebug(
                        "Livestream {Id} has {Count} participants, skipping cleanup",
                        stream.Id,
                        roomDetails!.ParticipantCount);
                    continue;
                }

                _logger.LogInformation(
                    "Marking livestream {Id} as ended - no participants in room after 5 minutes",
                    stream.Id);

                stream.Status = LiveStreamStatus.Ended;
                stream.EndedAt = now;

                _ = _liveKitService.BroadcastLivestreamUpdateAsync(
                    stream.RoomName,
                    "stream_ended",
                    new Dictionary<string, object>
                    {
                        { "livestream_id", stream.Id.ToString() },
                        { "ended_at", now.ToString() }
                    });

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
                cleanedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process livestream {Id}", stream.Id);
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Cleaned up {Count} stale livestreams", cleanedCount);
    }
}
