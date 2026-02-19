using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;
using FileService = DysonNetwork.Shared.Proto.FileService;

namespace DysonNetwork.Sphere.Live;

[ApiController]
[Route("api/livestreams")]
public class LiveStreamController(
    AppDatabase db,
    LiveStreamService liveStreamService,
    LiveKitLivestreamService liveKitService,
    PublisherService pub,
    FileService.FileServiceClient files
)
    : ControllerBase
{
    /// <summary>
    /// Sanitizes a live stream by removing sensitive fields (ingress/egress keys)
    /// that should only be visible to the stream owner
    /// </summary>
    private static SnLiveStream SanitizeForPublic(SnLiveStream liveStream)
    {
        liveStream.IngressStreamKey = null;
        liveStream.IngressId = null;
        liveStream.EgressId = null;
        return liveStream;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateLiveStreamRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));
        if (userPublishers.Count == 0)
        {
            return BadRequest("You need to be a member of a publisher to create live streams.");
        }

        var publisherId = userPublishers.First().Id;

        SnCloudFileReferenceObject? thumbnail = null;
        if (request.ThumbnailId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.ThumbnailId });
            if (file is null)
                return BadRequest("Thumbnail not found.");
            thumbnail = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        var liveStream = await liveStreamService.CreateAsync(
            publisherId,
            request.Title,
            request.Description,
            request.Slug,
            request.Type ?? Shared.Models.LiveStreamType.Regular,
            request.Visibility ?? Shared.Models.LiveStreamVisibility.Public,
            request.Metadata,
            thumbnail);

        return Ok(liveStream);
    }

    [HttpGet]
    public async Task<IActionResult> GetActive([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var liveStreams = await liveStreamService.GetActiveAsync(limit, offset);
        // Sanitize all streams for public viewing
        foreach (var stream in liveStreams)
        {
            SanitizeForPublic(stream);
        }
        return Ok(liveStreams);
    }

    [HttpGet("publisher/{publisherId:guid}")]
    public async Task<IActionResult> GetByPublisher(Guid publisherId, [FromQuery] int limit = 20,
        [FromQuery] int offset = 0)
    {
        var liveStreams = await liveStreamService.GetByPublisherAsync(publisherId, limit, offset);
        // Sanitize all streams for public viewing
        foreach (var stream in liveStreams)
        {
            SanitizeForPublic(stream);
        }
        return Ok(liveStreams);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound();
        }

        // Check if current user is authorized to see sensitive data
        bool isAuthorized = false;
        if (HttpContext.Items["CurrentUser"] is Account currentUser)
        {
            var accountId = Guid.Parse(currentUser.Id);
            isAuthorized = await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor);
        }

        if (!isAuthorized)
        {
            SanitizeForPublic(liveStream);
        }

        return Ok(liveStream);
    }

    [HttpGet("{id:guid}/token")]
    public async Task<IActionResult> GetToken(Guid id, [FromQuery] string? identity = null)
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        if (liveStream.Status != Shared.Models.LiveStreamStatus.Active)
        {
            return BadRequest(new { error = "LiveStream is not active" });
        }

        var isStreamer = false;
        string finalIdentity;

        if (HttpContext.Items["CurrentUser"] is Account currentUser)
        {
            var accountId = Guid.Parse(currentUser.Id);
            var isEditor = await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor);
            
            if (isEditor)
            {
                // Streamer is trying to get a token - check if they're already streaming
                // If they are, give them a viewer identity to avoid kicking themselves
                var streamerIdentity = $"streamer_{accountId:N}";
                if (identity == streamerIdentity || string.IsNullOrEmpty(identity))
                {
                    // User is the streamer trying to watch their own stream
                    // Give them a viewer identity instead
                    isStreamer = false;
                    finalIdentity = $"viewer_{accountId:N}";
                }
                else
                {
                    // User provided a different identity, use it as streamer
                    isStreamer = true;
                    finalIdentity = identity;
                }
            }
            else
            {
                // Regular viewer
                finalIdentity = identity ?? $"viewer_{accountId:N}";
            }
        }
        else
        {
            // Anonymous user
            finalIdentity = identity ?? $"viewer_{Guid.NewGuid():N}";
        }

        var token = liveKitService.GenerateToken(
            liveStream.RoomName,
            finalIdentity,
            canPublish: isStreamer,
            canSubscribe: true,
            metadata: new Dictionary<string, string>
            {
                { "livestream_id", id.ToString() },
                { "is_streamer", isStreamer.ToString().ToLower() }
            });

        return Ok(new
        {
            Token = token,
            liveStream.RoomName,
            Url = liveKitService.Host.Replace("http", "ws"),
            IsStreamer = isStreamer,
            Identity = finalIdentity
        });
    }

    [HttpPost("{id:guid}/start")]
    [Authorize]
    public async Task<IActionResult> StartStreaming(Guid id, [FromBody] StartStreamingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to start the live stream.");
        }

        var identity = $"streamer_{accountId:N}";
        var name = liveStream.Publisher?.Nick ?? "Streamer";

        var ingressResult = await liveStreamService.StartStreamingAsync(id, identity, name);

        return Ok(new
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage egress.");
        }

        var egressResult = await liveStreamService.StartEgressAsync(
            id,
            request.RtmpUrls,
            request.FilePath);

        return Ok(new { EgressId = egressResult!.EgressId });
    }

    [HttpPost("{id:guid}/egress/stop")]
    [Authorize]
    public async Task<IActionResult> StopEgress(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage egress.");
        }

        await liveStreamService.StopEgressAsync(id);

        return Ok();
    }

    [HttpPost("{id:guid}/end")]
    [Authorize]
    public async Task<IActionResult> End(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to end the live stream.");
        }

        await liveStreamService.EndAsync(id);

        return Ok();
    }

    [HttpGet("{id:guid}/details")]
    public async Task<IActionResult> GetRoomDetails(Guid id)
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound();
        }

        var details = await liveKitService.GetRoomDetailsAsync(liveStream.RoomName);
        if (details == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            ParticipantCount = details.ParticipantCount,
            ViewerCount = liveStream.ViewerCount,
            PeakViewerCount = liveStream.PeakViewerCount,
        });
    }

    [HttpPatch("{id:guid}/thumbnail")]
    [Authorize]
    public async Task<IActionResult> UpdateThumbnail(Guid id, [FromBody] UpdateThumbnailRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound();
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to update the thumbnail.");
        }

        if (request.ThumbnailId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.ThumbnailId });
            if (file is null)
                return BadRequest("Thumbnail not found.");

            liveStream.Thumbnail = SnCloudFileReferenceObject.FromProtoValue(file);
        }
        else
        {
            liveStream.Thumbnail = null;
        }

        await db.SaveChangesAsync();

        return Ok(liveStream);
    }
}

public record CreateLiveStreamRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
    public string? ThumbnailId { get; init; }
    public Shared.Models.LiveStreamType? Type { get; init; }
    public Shared.Models.LiveStreamVisibility? Visibility { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record UpdateThumbnailRequest
{
    public string? ThumbnailId { get; init; }
}

public record StartStreamingRequest
{
    public string? ParticipantName { get; init; }
}

public record StartEgressRequest
{
    public List<string>? RtmpUrls { get; init; }
    public string? FilePath { get; init; }
}
