using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Live;

public class LiveStreamService(
    AppDatabase db,
    LiveKitLivestreamService liveKitService,
    PublisherSubscriptionService publisherSubscriptionService,
    IServiceProvider serviceProvider,
    ILogger<LiveStreamService> logger
)
{
    public async Task<SnLiveStream> CreateAsync(
        Guid publisherId,
        string? title,
        string? description,
        string? slug,
        LiveStreamType type = LiveStreamType.Regular,
        LiveStreamVisibility visibility = LiveStreamVisibility.Public,
        Dictionary<string, object>? metadata = null,
        SnCloudFileReferenceObject? thumbnail = null)
    {
        var roomName = $"livestream_{Guid.NewGuid():N}";

        await liveKitService.CreateRoomAsync(roomName);

        var liveStream = new SnLiveStream
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Slug = slug,
            Type = type,
            Visibility = visibility,
            Status = LiveStreamStatus.Pending,
            RoomName = roomName,
            PublisherId = publisherId,
            Metadata = metadata,
            Thumbnail = thumbnail,
        };

        db.LiveStreams.Add(liveStream);
        await db.SaveChangesAsync();

        logger.LogInformation("Created LiveStream: {Id} for publisher: {PublisherId}", liveStream.Id, publisherId);

        return liveStream;
    }

    public async Task<SnLiveStream?> GetByIdAsync(Guid id)
    {
        return await db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.Id == id);
    }

    public async Task<SnLiveStream?> GetActiveByPublisherAsync(Guid publisherId)
    {
        return await db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.PublisherId == publisherId && ls.Status == LiveStreamStatus.Active);
    }

    public async Task<List<SnLiveStream>> GetActiveAsync(int limit = 50, int offset = 0)
    {
        return await db.LiveStreams
            .Include(ls => ls.Publisher)
            .Where(ls => ls.Status == LiveStreamStatus.Active && ls.Visibility == LiveStreamVisibility.Public)
            .OrderByDescending(ls => ls.StartedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<SnLiveStream>> GetByPublisherAsync(Guid publisherId, int limit = 20, int offset = 0)
    {
        return await db.LiveStreams
            .Include(ls => ls.Publisher)
            .Where(ls => ls.PublisherId == publisherId)
            .OrderByDescending(ls => ls.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<LiveKitIngressResult?> StartStreamingAsync(Guid id, string participantIdentity,
        string? participantName)
    {
        return await StartStreamingAsync(id, participantIdentity, participantName, createIngress: true);
    }

    public async Task<LiveKitIngressResult?> StartStreamingAsync(
        Guid id,
        string participantIdentity,
        string? participantName,
        bool createIngress = true,
        bool enableTranscoding = true,
        bool useWhipIngress = false)
    {
        var liveStream = await db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.Id == id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Pending && liveStream.Status != LiveStreamStatus.Ended)
        {
            throw new InvalidOperationException($"LiveStream is not in a startable state: {liveStream.Status}");
        }

        LiveKitIngressResult? ingressResult = null;

        if (createIngress)
        {
            if (useWhipIngress)
            {
                ingressResult = await liveKitService.CreateWhipIngressAsync(
                    liveStream.RoomName,
                    participantIdentity,
                    participantName,
                    liveStream.Title,
                    enableTranscoding);
            }
            else
            {
                ingressResult = await liveKitService.CreateIngressAsync(
                    liveStream.RoomName,
                    participantIdentity,
                    participantName,
                    liveStream.Title,
                    enableTranscoding);
            }

            liveStream.IngressId = ingressResult.IngressId;
            liveStream.IngressStreamKey = ingressResult.StreamKey;
        }

        liveStream.Status = LiveStreamStatus.Active;
        liveStream.StartedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();

        await liveKitService.BroadcastLivestreamUpdateAsync(
            liveStream.RoomName,
            "stream_started",
            new Dictionary<string, object>
            {
                { "livestream_id", liveStream.Id.ToString() },
                { "title", liveStream.Title ?? "" },
                { "started_at", liveStream.StartedAt.ToString() }
            });

        logger.LogInformation("Started streaming for LiveStream: {Id} (ingress: {HasIngress}, whip: {UseWhip})", 
            id, createIngress, useWhipIngress);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var scopedSubsService = scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await scopedSubsService.NotifySubscriberLiveStream(liveStream);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to notify subscribers about live stream {Id}", id);
            }
        });

        return ingressResult;
    }

    public async Task StartInAppStreamingAsync(Guid id)
    {
        var liveStream = await db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.Id == id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Pending && liveStream.Status != LiveStreamStatus.Ended)
        {
            throw new InvalidOperationException($"LiveStream is not in a startable state: {liveStream.Status}");
        }

        liveStream.Status = LiveStreamStatus.Active;
        liveStream.StartedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();

        await liveKitService.BroadcastLivestreamUpdateAsync(
            liveStream.RoomName,
            "stream_started",
            new Dictionary<string, object>
            {
                { "livestream_id", liveStream.Id.ToString() },
                { "title", liveStream.Title ?? "" },
                { "started_at", liveStream.StartedAt.ToString() }
            });

        logger.LogInformation("Started in-app streaming for LiveStream: {Id}", id);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var scopedSubsService = scope.ServiceProvider.GetRequiredService<PublisherSubscriptionService>();
                await scopedSubsService.NotifySubscriberLiveStream(liveStream);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to notify subscribers about live stream {Id}", id);
            }
        });
    }

    public async Task<LiveKitEgressResult?> StartEgressAsync(Guid id, List<string>? rtmpUrls = null,
        string? filePath = null)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Active)
        {
            throw new InvalidOperationException($"LiveStream is not active: {liveStream.Status}");
        }

        var egressResult = await liveKitService.StartRoomCompositeEgressAsync(
            liveStream.RoomName,
            rtmpUrls,
            filePath);

        liveStream.EgressId = egressResult.EgressId;
        await db.SaveChangesAsync();

        logger.LogInformation("Started egress for LiveStream: {Id}, egressId: {EgressId}", id, egressResult.EgressId);

        return egressResult;
    }

    public async Task<LiveKitHlsEgressResult> StartHlsEgressAsync(
        Guid id,
        string playlistName = "playlist.m3u8",
        uint segmentDuration = 6,
        int segmentCount = 0,
        string? layout = null)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Active)
        {
            throw new InvalidOperationException($"LiveStream is not active: {liveStream.Status}");
        }

        if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
        {
            throw new InvalidOperationException("HLS egress is already active for this stream");
        }

        var filePath = $"hls/{liveStream.Id}/{playlistName}";
        var egressResult = await liveKitService.StartRoomCompositeHlsEgressAsync(
            liveStream.RoomName,
            playlistName,
            filePath,
            segmentDuration,
            segmentCount,
            layout);

        liveStream.HlsEgressId = egressResult.EgressId;
        liveStream.HlsStartedAt = SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();

        logger.LogInformation("Started HLS egress for LiveStream: {Id}, egressId: {EgressId}", id,
            egressResult.EgressId);

        return egressResult;
    }

    public async Task StopHlsEgressAsync(Guid id)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
        {
            try
            {
                await liveKitService.StopEgressAsync(liveStream.HlsEgressId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop HLS egress {EgressId}, marking as stopped anyway", liveStream.HlsEgressId);
            }
            liveStream.HlsEgressId = null;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Stopped HLS egress for LiveStream: {Id}", id);
    }

    public async Task StopEgressAsync(Guid id)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.EgressId))
        {
            try
            {
                await liveKitService.StopEgressAsync(liveStream.EgressId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop egress {EgressId}, marking as stopped anyway", liveStream.EgressId);
            }
            liveStream.EgressId = null;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Stopped egress for LiveStream: {Id}", id);
    }

    public async Task EndAsync(Guid id)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.IngressId))
        {
            try
            {
                await liveKitService.DeleteIngressAsync(liveStream.IngressId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete ingress {IngressId}, marking as deleted anyway", liveStream.IngressId);
            }
            liveStream.IngressId = null;
        }

        if (!string.IsNullOrEmpty(liveStream.EgressId))
        {
            try
            {
                await liveKitService.StopEgressAsync(liveStream.EgressId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop egress {EgressId}, marking as stopped anyway", liveStream.EgressId);
            }
            liveStream.EgressId = null;
        }

        if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
        {
            try
            {
                await liveKitService.StopEgressAsync(liveStream.HlsEgressId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop HLS egress {EgressId}, marking as stopped anyway", liveStream.HlsEgressId);
            }
            liveStream.HlsEgressId = null;
        }

        liveStream.Status = LiveStreamStatus.Ended;
        liveStream.EndedAt = SystemClock.Instance.GetCurrentInstant();

        if (liveStream.StartedAt.HasValue && liveStream.EndedAt.HasValue)
        {
            var sessionDuration = liveStream.EndedAt.Value - liveStream.StartedAt.Value;
            liveStream.TotalDurationSeconds += (long)sessionDuration.TotalSeconds;
        }

        await db.SaveChangesAsync();

        await liveKitService.BroadcastLivestreamUpdateAsync(
            liveStream.RoomName,
            "stream_ended",
            new Dictionary<string, object>
            {
                { "livestream_id", liveStream.Id.ToString() },
                { "ended_at", liveStream.EndedAt.ToString() }
            });

        logger.LogInformation("Ended LiveStream: {Id}", id);
    }

    public async Task UpdateViewerCountAsync(Guid id, int count)
    {
        var liveStream = await db.LiveStreams.FindAsync(id);
        if (liveStream == null) return;

        liveStream.ViewerCount = count;
        if (count > liveStream.PeakViewerCount)
        {
            liveStream.PeakViewerCount = count;
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(Guid id, LiveStreamStatus status)
    {
        var liveStream = await db.LiveStreams.FindAsync(id);
        if (liveStream == null) return;

        liveStream.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var liveStream = await db.LiveStreams.FindAsync(id)
                         ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        // Clean up LiveKit resources if stream is active
        if (liveStream.Status == LiveStreamStatus.Active)
        {
            if (!string.IsNullOrEmpty(liveStream.IngressId))
            {
                try
                {
                    await liveKitService.DeleteIngressAsync(liveStream.IngressId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete ingress {IngressId} during stream deletion", liveStream.IngressId);
                }
            }

            if (!string.IsNullOrEmpty(liveStream.EgressId))
            {
                try
                {
                    await liveKitService.StopEgressAsync(liveStream.EgressId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to stop egress {EgressId} during stream deletion", liveStream.EgressId);
                }
            }

            if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
            {
                try
                {
                    await liveKitService.StopEgressAsync(liveStream.HlsEgressId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to stop HLS egress {EgressId} during stream deletion", liveStream.HlsEgressId);
                }
            }

            try
            {
                await liveKitService.DeleteRoomAsync(liveStream.RoomName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete room {RoomName} during stream deletion", liveStream.RoomName);
            }
        }

        db.LiveStreams.Remove(liveStream);
        await db.SaveChangesAsync();

        logger.LogInformation("Deleted LiveStream: {Id}", id);
    }
}