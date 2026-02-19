using System.Security.Claims;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Sphere.Live;

[ApiController]
[Route("api/livestreams")]
public class LiveStreamController : ControllerBase
{
    private readonly LiveStreamService _liveStreamService;
    private readonly LiveKitLivestreamService _liveKitService;
    private readonly ILogger<LiveStreamController> _logger;

    public LiveStreamController(
        LiveStreamService liveStreamService,
        LiveKitLivestreamService liveKitService,
        ILogger<LiveStreamController> logger)
    {
        _liveStreamService = liveStreamService;
        _liveKitService = liveKitService;
        _logger = logger;
    }

    private Guid? GetPublisherId()
    {
        var publisherIdClaim = User.FindFirstValue("publisher_id");
        if (publisherIdClaim != null && Guid.TryParse(publisherIdClaim, out var publisherId))
        {
            return publisherId;
        }
        return null;
    }

    private Guid? GetAccountId()
    {
        var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (accountIdClaim != null && Guid.TryParse(accountIdClaim, out var accountId))
        {
            return accountId;
        }
        return null;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateLiveStreamRequest request)
    {
        var publisherId = GetPublisherId();
        if (publisherId == null)
        {
            return Unauthorized(new { error = "Publisher ID not found in token" });
        }

        var liveStream = await _liveStreamService.CreateAsync(
            publisherId.Value,
            request.Title,
            request.Description,
            request.Slug,
            request.Type ?? LiveStreamType.Regular,
            request.Visibility ?? LiveStreamVisibility.Public,
            request.Metadata);

        return Ok(new CreateLiveStreamResponse
        {
            Id = liveStream.Id,
            RoomName = liveStream.RoomName,
            Title = liveStream.Title,
            Description = liveStream.Description,
            Slug = liveStream.Slug,
            Type = liveStream.Type,
            Visibility = liveStream.Visibility,
            Status = liveStream.Status,
            CreatedAt = liveStream.CreatedAt,
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetActive([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var liveStreams = await _liveStreamService.GetActiveAsync(limit, offset);

        return Ok(liveStreams.Select(ls => new LiveStreamResponse
        {
            Id = ls.Id,
            Title = ls.Title,
            Description = ls.Description,
            Slug = ls.Slug,
            RoomName = ls.RoomName,
            Type = ls.Type,
            Visibility = ls.Visibility,
            Status = ls.Status,
            ViewerCount = ls.ViewerCount,
            PeakViewerCount = ls.PeakViewerCount,
            StartedAt = ls.StartedAt,
            CreatedAt = ls.CreatedAt,
            Publisher = ls.Publisher != null ? new PublisherInfo
            {
                Id = ls.Publisher.Id,
                Name = ls.Publisher.Name,
                Nick = ls.Publisher.Nick,
                Picture = ls.Publisher.Picture,
            } : null,
        }));
    }

    [HttpGet("publisher/{publisherId:guid}")]
    public async Task<IActionResult> GetByPublisher(Guid publisherId, [FromQuery] int limit = 20, [FromQuery] int offset = 0)
    {
        var liveStreams = await _liveStreamService.GetByPublisherAsync(publisherId, limit, offset);

        return Ok(liveStreams.Select(ls => new LiveStreamResponse
        {
            Id = ls.Id,
            Title = ls.Title,
            Description = ls.Description,
            Slug = ls.Slug,
            RoomName = ls.RoomName,
            Type = ls.Type,
            Visibility = ls.Visibility,
            Status = ls.Status,
            ViewerCount = ls.ViewerCount,
            PeakViewerCount = ls.PeakViewerCount,
            StartedAt = ls.StartedAt,
            EndedAt = ls.EndedAt,
            CreatedAt = ls.CreatedAt,
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound();
        }

        return Ok(new LiveStreamDetailResponse
        {
            Id = liveStream.Id,
            Title = liveStream.Title,
            Description = liveStream.Description,
            Slug = liveStream.Slug,
            RoomName = liveStream.RoomName,
            Type = liveStream.Type,
            Visibility = liveStream.Visibility,
            Status = liveStream.Status,
            ViewerCount = liveStream.ViewerCount,
            PeakViewerCount = liveStream.PeakViewerCount,
            StartedAt = liveStream.StartedAt,
            EndedAt = liveStream.EndedAt,
            CreatedAt = liveStream.CreatedAt,
            RtmpUrl = liveStream.Status == LiveStreamStatus.Active ? $"rtmp://{_liveKitService.GetType().GetProperty("Host")}" : null,
            Publisher = liveStream.Publisher != null ? new PublisherInfo
            {
                Id = liveStream.Publisher.Id,
                Name = liveStream.Publisher.Name,
                Nick = liveStream.Publisher.Nick,
                Picture = liveStream.Publisher.Picture,
            } : null,
        });
    }

    [HttpGet("{id:guid}/token")]
    public async Task<IActionResult> GetToken(Guid id, [FromQuery] string? identity = null)
    {
        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.Status != LiveStreamStatus.Active)
        {
            return BadRequest(new { error = "LiveStream is not active" });
        }

        var isStreamer = false;
        if (identity == null)
        {
            var accountId = GetAccountId();
            var publisherId = GetPublisherId();
            
            if (publisherId.HasValue && liveStream.PublisherId == publisherId.Value)
            {
                isStreamer = true;
                identity = $"streamer_{accountId ?? Guid.NewGuid():N}";
            }
            else
            {
                identity = $"viewer_{accountId ?? Guid.NewGuid():N}";
            }
        }
        else
        {
            isStreamer = identity.StartsWith("streamer_");
        }

        var token = _liveKitService.GenerateToken(
            liveStream.RoomName,
            identity,
            canPublish: isStreamer,
            canSubscribe: true,
            metadata: new Dictionary<string, string>
            {
                { "livestream_id", id.ToString() },
                { "is_streamer", isStreamer.ToString().ToLower() }
            });

        return Ok(new GetTokenResponse
        {
            Token = token,
            RoomName = liveStream.RoomName,
            Url = _liveKitService.Host.Replace("http", "wss"),
        });
    }

    [HttpPost("{id:guid}/start")]
    [Authorize]
    public async Task<IActionResult> StartStreaming(Guid id, [FromBody] StartStreamingRequest request)
    {
        var publisherId = GetPublisherId();
        var accountId = GetAccountId();
        
        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.PublisherId != publisherId)
        {
            return Forbid();
        }

        var identity = $"streamer_{accountId ?? Guid.NewGuid():N}";
        var name = liveStream.Publisher?.Nick ?? "Streamer";

        var ingressResult = await _liveStreamService.StartStreamingAsync(id, identity, name);

        return Ok(new StartStreamingResponse
        {
            RtmpUrl = ingressResult.Url,
            StreamKey = ingressResult.StreamKey,
            RoomName = liveStream.RoomName,
        });
    }

    [HttpPost("{id:guid}/egress")]
    [Authorize]
    public async Task<IActionResult> StartEgress(Guid id, [FromBody] StartEgressRequest request)
    {
        var publisherId = GetPublisherId();

        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.PublisherId != publisherId)
        {
            return Forbid();
        }

        var egressResult = await _liveStreamService.StartEgressAsync(
            id,
            request.RtmpUrls,
            request.FilePath);

        return Ok(new StartEgressResponse
        {
            EgressId = egressResult!.EgressId,
        });
    }

    [HttpPost("{id:guid}/egress/stop")]
    [Authorize]
    public async Task<IActionResult> StopEgress(Guid id)
    {
        var publisherId = GetPublisherId();

        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.PublisherId != publisherId)
        {
            return Forbid();
        }

        await _liveStreamService.StopEgressAsync(id);

        return Ok();
    }

    [HttpPost("{id:guid}/end")]
    [Authorize]
    public async Task<IActionResult> End(Guid id)
    {
        var publisherId = GetPublisherId();

        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.PublisherId != publisherId)
        {
            return Forbid();
        }

        await _liveStreamService.EndAsync(id);

        return Ok();
    }

    [HttpGet("{id:guid}/details")]
    public async Task<IActionResult> GetRoomDetails(Guid id)
    {
        var liveStream = await _liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound();
        }

        var details = await _liveKitService.GetRoomDetailsAsync(liveStream.RoomName);
        if (details == null)
        {
            return NotFound();
        }

        return Ok(new RoomDetailsResponse
        {
            ParticipantCount = details.ParticipantCount,
            ViewerCount = liveStream.ViewerCount,
            PeakViewerCount = liveStream.PeakViewerCount,
        });
    }
}

public record CreateLiveStreamRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public LiveStreamType? Type { get; init; }
    public LiveStreamVisibility? Visibility { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record CreateLiveStreamResponse
{
    public Guid Id { get; init; }
    public string RoomName { get; init; } = null!;
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public LiveStreamType Type { get; init; }
    public LiveStreamVisibility Visibility { get; init; }
    public LiveStreamStatus Status { get; init; }
    public Instant CreatedAt { get; init; }
}

public record LiveStreamResponse
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public string RoomName { get; init; } = null!;
    public LiveStreamType Type { get; init; }
    public LiveStreamVisibility Visibility { get; init; }
    public LiveStreamStatus Status { get; init; }
    public int ViewerCount { get; init; }
    public int PeakViewerCount { get; init; }
    public Instant? StartedAt { get; init; }
    public Instant? EndedAt { get; init; }
    public Instant CreatedAt { get; init; }
    public PublisherInfo? Publisher { get; init; }
}

public record LiveStreamDetailResponse : LiveStreamResponse
{
    public string? RtmpUrl { get; init; }
}

public record PublisherInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Nick { get; init; } = null!;
    public SnCloudFileReferenceObject? Picture { get; init; }
}

public record StartStreamingRequest
{
    public string? ParticipantName { get; init; }
}

public record StartStreamingResponse
{
    public string RtmpUrl { get; init; } = null!;
    public string StreamKey { get; init; } = null!;
    public string RoomName { get; init; } = null!;
}

public record StartEgressRequest
{
    public List<string>? RtmpUrls { get; init; }
    public string? FilePath { get; init; }
}

public record StartEgressResponse
{
    public string EgressId { get; init; } = null!;
}

public record GetTokenResponse
{
    public string Token { get; init; } = null!;
    public string RoomName { get; init; } = null!;
    public string Url { get; init; } = null!;
}

public record RoomDetailsResponse
{
    public int ParticipantCount { get; init; }
    public int ViewerCount { get; init; }
    public int PeakViewerCount { get; init; }
}
