using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Live;

public class LiveStreamService
{
    private readonly AppDatabase _db;
    private readonly LiveKitLivestreamService _liveKitService;
    private readonly ILogger<LiveStreamService> _logger;

    public LiveStreamService(
        AppDatabase db,
        LiveKitLivestreamService liveKitService,
        ILogger<LiveStreamService> logger)
    {
        _db = db;
        _liveKitService = liveKitService;
        _logger = logger;
    }

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
        
        await _liveKitService.CreateRoomAsync(roomName);

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

        _db.LiveStreams.Add(liveStream);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created LiveStream: {Id} for publisher: {PublisherId}", liveStream.Id, publisherId);

        return liveStream;
    }

    public async Task<SnLiveStream?> GetByIdAsync(Guid id)
    {
        return await _db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.Id == id);
    }

    public async Task<SnLiveStream?> GetActiveByPublisherAsync(Guid publisherId)
    {
        return await _db.LiveStreams
            .Include(ls => ls.Publisher)
            .FirstOrDefaultAsync(ls => ls.PublisherId == publisherId && ls.Status == LiveStreamStatus.Active);
    }

    public async Task<List<SnLiveStream>> GetActiveAsync(int limit = 50, int offset = 0)
    {
        return await _db.LiveStreams
            .Include(ls => ls.Publisher)
            .Where(ls => ls.Status == LiveStreamStatus.Active && ls.Visibility == LiveStreamVisibility.Public)
            .OrderByDescending(ls => ls.StartedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<SnLiveStream>> GetByPublisherAsync(Guid publisherId, int limit = 20, int offset = 0)
    {
        return await _db.LiveStreams
            .Include(ls => ls.Publisher)
            .Where(ls => ls.PublisherId == publisherId)
            .OrderByDescending(ls => ls.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<LiveKitIngressResult?> StartStreamingAsync(Guid id, string participantIdentity, string? participantName)
    {
        return await StartStreamingAsync(id, participantIdentity, participantName, createIngress: true);
    }

    public async Task<LiveKitIngressResult?> StartStreamingAsync(
        Guid id, 
        string participantIdentity, 
        string? participantName,
        bool createIngress = true)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Pending && liveStream.Status != LiveStreamStatus.Ended)
        {
            throw new InvalidOperationException($"LiveStream is not in a startable state: {liveStream.Status}");
        }

        LiveKitIngressResult? ingressResult = null;

        if (createIngress)
        {
            ingressResult = await _liveKitService.CreateIngressAsync(
                liveStream.RoomName,
                participantIdentity,
                participantName,
                liveStream.Title);

            liveStream.IngressId = ingressResult.IngressId;
            liveStream.IngressStreamKey = ingressResult.StreamKey;
        }

        liveStream.Status = LiveStreamStatus.Active;
        liveStream.StartedAt = SystemClock.Instance.GetCurrentInstant();

        await _db.SaveChangesAsync();

        _logger.LogInformation("Started streaming for LiveStream: {Id} (ingress: {HasIngress})", id, createIngress);

        return ingressResult;
    }

    public async Task StartInAppStreamingAsync(Guid id)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Pending && liveStream.Status != LiveStreamStatus.Ended)
        {
            throw new InvalidOperationException($"LiveStream is not in a startable state: {liveStream.Status}");
        }

        liveStream.Status = LiveStreamStatus.Active;
        liveStream.StartedAt = SystemClock.Instance.GetCurrentInstant();

        await _db.SaveChangesAsync();

        _logger.LogInformation("Started in-app streaming for LiveStream: {Id}", id);
    }

    public async Task<LiveKitEgressResult?> StartEgressAsync(Guid id, List<string>? rtmpUrls = null, string? filePath = null)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (liveStream.Status != LiveStreamStatus.Active)
        {
            throw new InvalidOperationException($"LiveStream is not active: {liveStream.Status}");
        }

        var egressResult = await _liveKitService.StartRoomCompositeEgressAsync(
            liveStream.RoomName,
            rtmpUrls,
            filePath);

        liveStream.EgressId = egressResult.EgressId;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Started egress for LiveStream: {Id}, egressId: {EgressId}", id, egressResult.EgressId);

        return egressResult;
    }

    public async Task<LiveKitHlsEgressResult> StartHlsEgressAsync(
        Guid id,
        string playlistName = "playlist.m3u8",
        uint segmentDuration = 6,
        int segmentCount = 0,
        string? layout = null)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
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
        var egressResult = await _liveKitService.StartRoomCompositeHlsEgressAsync(
            liveStream.RoomName,
            playlistName,
            filePath,
            segmentDuration,
            segmentCount,
            layout);

        liveStream.HlsEgressId = egressResult.EgressId;
        liveStream.HlsStartedAt = SystemClock.Instance.GetCurrentInstant();
        
        await _db.SaveChangesAsync();

        _logger.LogInformation("Started HLS egress for LiveStream: {Id}, egressId: {EgressId}", id, egressResult.EgressId);

        return egressResult;
    }

    public async Task StopHlsEgressAsync(Guid id)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
        {
            await _liveKitService.StopEgressAsync(liveStream.HlsEgressId);
            liveStream.HlsEgressId = null;
            liveStream.HlsPlaylistUrl = null;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Stopped HLS egress for LiveStream: {Id}", id);
    }

    public async Task StopEgressAsync(Guid id)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.EgressId))
        {
            await _liveKitService.StopEgressAsync(liveStream.EgressId);
            liveStream.EgressId = null;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Stopped egress for LiveStream: {Id}", id);
    }

    public async Task EndAsync(Guid id)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        if (!string.IsNullOrEmpty(liveStream.IngressId))
        {
            await _liveKitService.DeleteIngressAsync(liveStream.IngressId);
            liveStream.IngressId = null;
        }

        if (!string.IsNullOrEmpty(liveStream.EgressId))
        {
            await _liveKitService.StopEgressAsync(liveStream.EgressId);
            liveStream.EgressId = null;
        }

        if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
        {
            await _liveKitService.StopEgressAsync(liveStream.HlsEgressId);
            liveStream.HlsEgressId = null;
            liveStream.HlsPlaylistUrl = null;
        }

        liveStream.Status = LiveStreamStatus.Ended;
        liveStream.EndedAt = SystemClock.Instance.GetCurrentInstant();

        await _db.SaveChangesAsync();

        _logger.LogInformation("Ended LiveStream: {Id}", id);
    }

    public async Task UpdateViewerCountAsync(Guid id, int count)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id);
        if (liveStream == null) return;

        liveStream.ViewerCount = count;
        if (count > liveStream.PeakViewerCount)
        {
            liveStream.PeakViewerCount = count;
        }

        await _db.SaveChangesAsync();
    }

    public async Task UpdateStatusAsync(Guid id, LiveStreamStatus status)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id);
        if (liveStream == null) return;

        liveStream.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var liveStream = await _db.LiveStreams.FindAsync(id)
            ?? throw new InvalidOperationException($"LiveStream not found: {id}");

        // Clean up LiveKit resources if stream is active
        if (liveStream.Status == LiveStreamStatus.Active)
        {
            if (!string.IsNullOrEmpty(liveStream.IngressId))
            {
                await _liveKitService.DeleteIngressAsync(liveStream.IngressId);
            }

            if (!string.IsNullOrEmpty(liveStream.EgressId))
            {
                await _liveKitService.StopEgressAsync(liveStream.EgressId);
            }

            if (!string.IsNullOrEmpty(liveStream.HlsEgressId))
            {
                await _liveKitService.StopEgressAsync(liveStream.HlsEgressId);
            }

            await _liveKitService.DeleteRoomAsync(liveStream.RoomName);
        }

        _db.LiveStreams.Remove(liveStream);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted LiveStream: {Id}", id);
    }
}
