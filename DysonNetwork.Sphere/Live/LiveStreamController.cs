using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
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
    FileService.FileServiceClient files,
    RemotePaymentService remotePayments,
    ActionLogService.ActionLogServiceClient als,
    RemoteAccountService remoteAccounts
)
    : ControllerBase
{
    /// <summary>
    /// Sanitizes a live stream by removing sensitive fields (ingress/egress keys)
    /// that should only be visible to the stream owner
    /// </summary>
    private SnLiveStream SanitizeForPublic(SnLiveStream liveStream)
    {
        if (!string.IsNullOrEmpty(liveStream.HlsPlaylistPath))
        {
            liveStream.HlsPlaylistPath = liveKitService.BuildHlsPlaylistUrl(liveStream.HlsPlaylistPath);
        }

        liveStream.IngressStreamKey = null;
        liveStream.IngressId = null;
        liveStream.EgressId = null;
        return liveStream;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        [FromBody] CreateLiveStreamRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        SnPublisher? publisher;
        if (string.IsNullOrWhiteSpace(pubName))
        {
            var userPublishers = await pub.GetUserPublishers(accountId);
            if (userPublishers.Count == 0)
            {
                return BadRequest("You need to be a member of a publisher to create live streams.");
            }
            publisher = userPublishers.First();
        }
        else
        {
            publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return BadRequest("Publisher not found.");
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You need at least be an editor to create live streams for this publisher.");
        }

        SnCloudFileReferenceObject? thumbnail = null;
        if (request.ThumbnailId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.ThumbnailId });
            if (file is null)
                return BadRequest("Thumbnail not found.");
            thumbnail = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        var liveStream = await liveStreamService.CreateAsync(
            publisher.Id,
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
            isAuthorized = await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor);
        }

        if (!isAuthorized)
        {
            SanitizeForPublic(liveStream);
        }

        return Ok(liveStream);
    }

    [HttpGet("{id:guid}/token")]
    public async Task<IActionResult> GetToken(Guid id, [FromQuery(Name = "streamer")] bool isStreamer = false)
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        if (liveStream.Status != Shared.Models.LiveStreamStatus.Active)
            return BadRequest(new { error = "LiveStream is not active" });

        string identity;
        string? name = null;

        if (HttpContext.Items["CurrentUser"] is Account currentUser)
        {
            var accountId = Guid.Parse(currentUser.Id);
            var isEditor =
                await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor);

            if (isEditor && isStreamer)
            {
                isStreamer = true;
            }

            identity = currentUser.Name ?? $"user_{accountId:N}";
            name = currentUser.Nick;
        }
        else
        {
            return Unauthorized();
        }
        
        var token = liveKitService.GenerateToken(
            liveStream.RoomName,
            identity,
            name,
            canPublish: isStreamer,
            canSubscribe: true,
            metadata: new Dictionary<string, string>
            {
                { "livestream_id", id.ToString() },
                { "is_streamer", isStreamer.ToString().ToLower() },
                { "account_id", currentUser.Id }
            });

        return Ok(new
        {
            Token = token,
            liveStream.RoomName,
            Url = liveKitService.Host.Replace("http", "ws"),
            IsStreamer = isStreamer,
            Identity = identity
        });
    }

    [HttpPost("{id:guid}/start")]
    [Authorize]
    public async Task<IActionResult> StartStreaming(Guid id, [FromBody] StartStreamingRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to start the live stream.");
        }

        var identity = $"streamer_{accountId:N}_ingress";
        var name = request.ParticipantName ?? liveStream.Publisher?.Nick ?? "streamer";

        var noIngress = request.NoIngress ?? false;
        var useWhip = request.UseWhip ?? false;
        var enableTranscoding = request.EnableTranscoding ?? true;

        if (noIngress)
        {
            await liveStreamService.StartInAppStreamingAsync(id);

            return Ok(new
            {
                liveStream.RoomName,
                Url = liveKitService.Host.Replace("http", "ws"),
            });
        }
        else
        {
            var ingressResult = await liveStreamService.StartStreamingAsync(
                id, 
                identity, 
                name, 
                createIngress: true,
                enableTranscoding: enableTranscoding,
                useWhipIngress: useWhip);

            return Ok(new
            {
                Url = ingressResult!.Url,
                StreamKey = ingressResult.StreamKey,
                liveStream.RoomName,
            });
        }
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
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage egress.");
        }

        if (!currentUser.IsSuperuser)
        {
            return StatusCode(403, "Egress settings can only be configured by administrators.");
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
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage egress.");
        }

        await liveStreamService.StopEgressAsync(id);

        return Ok();
    }

    [HttpPost("{id:guid}/hls")]
    [Authorize]
    public async Task<IActionResult> StartHlsEgress(Guid id, [FromBody] StartHlsEgressRequest? request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage HLS egress.");
        }

        if (!currentUser.IsSuperuser)
        {
            return StatusCode(403, "HLS egress settings can only be configured by administrators.");
        }

        var playlistName = request?.PlaylistName ?? "playlist.m3u8";
        var segmentDuration = request?.SegmentDuration ?? 6;
        var segmentCount = request?.SegmentCount ?? 0;
        var layout = request?.Layout;

        var egressResult = await liveStreamService.StartHlsEgressAsync(
            id,
            playlistName,
            segmentDuration,
            segmentCount,
            layout);

        var playlistPath = $"{liveStream.Id}/{playlistName}";
        liveStream.HlsPlaylistPath = playlistPath;
        await db.SaveChangesAsync();

        var playlistUrl = liveKitService.BuildHlsPlaylistUrl(playlistPath);

        return Ok(new
        {
            EgressId = egressResult.EgressId,
            PlaylistUrl = playlistUrl,
            PlaylistName = playlistName,
        });
    }

    [HttpPost("{id:guid}/hls/stop")]
    [Authorize]
    public async Task<IActionResult> StopHlsEgress(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to manage HLS egress.");
        }

        await liveStreamService.StopHlsEgressAsync(id);

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
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to end the live stream.");
        }

        await liveStreamService.EndAsync(id);

        return Ok();
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLiveStreamRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to update the live stream.");
        }

        if (request.Title is not null)
            liveStream.Title = request.Title;

        if (request.Description is not null)
            liveStream.Description = request.Description;

        if (request.Slug is not null)
            liveStream.Slug = request.Slug;

        if (request.Type.HasValue)
            liveStream.Type = request.Type.Value;

        if (request.Visibility.HasValue)
            liveStream.Visibility = request.Visibility.Value;

        if (request.Metadata is not null)
            liveStream.Metadata = request.Metadata;

        await db.SaveChangesAsync();

        await liveKitService.BroadcastLivestreamUpdateAsync(
            liveStream.RoomName,
            "stream_updated",
            new Dictionary<string, object>
            {
                { "livestream_id", liveStream.Id.ToString() },
                { "title", liveStream.Title ?? "" },
                { "description", liveStream.Description ?? "" }
            });

        return Ok(SanitizeForPublic(liveStream));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
        {
            return NotFound(new { error = "LiveStream not found" });
        }

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to delete the live stream.");
        }

        await liveStreamService.DeleteAsync(id);

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
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId,
                Shared.Models.PublisherMemberRole.Editor))
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

        return Ok(SanitizeForPublic(liveStream));
    }

    [HttpGet("{id:guid}/chat")]
    [Authorize]
    public async Task<IActionResult> GetChatMessages(Guid id, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var messages = await db.LiveStreamChatMessages
            .Where(m => m.LiveStreamId == id && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
        var senders = await remoteAccounts.GetAccountBatch(senderIds);
        var sendersDict = senders.ToDictionary(a => Guid.Parse(a.Id));

        foreach (var message in messages)
        {
            if (sendersDict.TryGetValue(message.SenderId, out var sender))
                message.Sender = SnAccount.FromProtoValue(sender);
        }

        return Ok(messages);
    }

    [HttpPost("{id:guid}/chat")]
    [Authorize]
    public async Task<IActionResult> SendChatMessage(Guid id, [FromBody] SendChatMessageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        if (liveStream.Status != Shared.Models.LiveStreamStatus.Active)
            return BadRequest(new { error = "Stream is not active" });

        var accountId = Guid.Parse(currentUser.Id);

        var isTimeout = await db.LiveStreamChatMessages
            .AnyAsync(m => m.LiveStreamId == id && m.SenderId == accountId && m.TimeoutUntil > SystemClock.Instance.GetCurrentInstant());

        if (isTimeout)
        {
            return StatusCode(403, new { error = "You are temporarily muted" });
        }

        var message = new SnLiveStreamChatMessage
        {
            LiveStreamId = id,
            SenderId = accountId,
            SenderName = currentUser.Nick ?? currentUser.Name ?? "Anonymous",
            Content = request.Content,
        };

        db.LiveStreamChatMessages.Add(message);
        await db.SaveChangesAsync();

        var chatData = new
        {
            id = message.Id.ToString(),
            senderId = message.SenderId.ToString(),
            senderName = message.SenderName,
            content = message.Content,
            createdAt = message.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(chatData);
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        await liveKitService.SendDataAsync(liveStream.RoomName, data);

        return Ok(message);
    }

    [HttpDelete("{id:guid}/chat/{messageId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteChatMessage(Guid id, Guid messageId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to moderate chat.");
        }

        var message = await db.LiveStreamChatMessages.FindAsync(messageId);
        if (message == null || message.LiveStreamId != id)
            return NotFound(new { error = "Message not found" });

        message.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id:guid}/chat/{messageId:guid}/timeout")]
    [Authorize]
    public async Task<IActionResult> TimeoutUser(Guid id, Guid messageId, [FromBody] TimeoutRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(liveStream.PublisherId!.Value, accountId, PublisherMemberRole.Editor))
        {
            return StatusCode(403, "You need to be an editor of this publisher to timeout users.");
        }

        var message = await db.LiveStreamChatMessages.FindAsync(messageId);
        if (message == null || message.LiveStreamId != id)
            return NotFound(new { error = "Message not found" });

        var durationMinutes = request.DurationMinutes ?? 10;
        message.TimeoutUntil = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(durationMinutes));
        await db.SaveChangesAsync();

        var timeoutData = new
        {
            type = "timeout",
            senderId = message.SenderId.ToString(),
            durationMinutes = durationMinutes,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(timeoutData);
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        await liveKitService.SendDataAsync(liveStream.RoomName, data);

        return Ok();
    }

    [HttpGet("{id:guid}/awards")]
    public async Task<IActionResult> GetLiveStreamAwards(
        Guid id,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0
    )
    {
        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var query = db.LiveStreamAwards.Where(a => a.LiveStreamId == id);

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var awards = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return Ok(awards);
    }

    public class LiveStreamAwardRequest
    {
        public decimal Amount { get; init; }
        public Shared.Models.LiveStreamAwardAttitude Attitude { get; init; }
        [System.ComponentModel.DataAnnotations.MaxLength(4096)]
        public string? Message { get; init; }
    }

    public class LiveStreamAwardResponse
    {
        public Guid OrderId { get; set; }
    }

    [HttpPost("{id:guid}/awards")]
    [Authorize]
    public async Task<IActionResult> AwardLiveStream(
        Guid id,
        [FromBody] LiveStreamAwardRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        if (request.Attitude == Shared.Models.LiveStreamAwardAttitude.Neutral)
            return BadRequest("You cannot create a neutral live stream award");

        var liveStream = await liveStreamService.GetByIdAsync(id);
        if (liveStream == null)
            return NotFound(new { error = "LiveStream not found" });

        var accountId = Guid.Parse(currentUser.Id);

        var orderRemark = string.IsNullOrWhiteSpace(liveStream.Title)
            ? "from @" + liveStream.Publisher?.Name
            : liveStream.Title;

        var order = await remotePayments.CreateOrder(
            currency: "points",
            amount: request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            productIdentifier: "livestreams.award",
            remarks: $"Award livestream {orderRemark}",
            meta: DysonNetwork.Shared.Data.InfraObjectCoder.ConvertObjectToByteString(
                new Dictionary<string, object?>
                {
                    ["account_id"] = accountId,
                    ["livestream_id"] = liveStream.Id,
                    ["amount"] = request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["message"] = request.Message,
                    ["attitude"] = request.Attitude,
                }
            ).ToByteArray()
        );

        return Ok(new LiveStreamAwardResponse { OrderId = Guid.Parse(order.Id) });
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

public record UpdateLiveStreamRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Slug { get; init; }
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
    public bool? NoIngress { get; set; }
    public bool? UseWhip { get; init; }
    public bool? EnableTranscoding { get; init; }
}

public record StartEgressRequest
{
    public List<string>? RtmpUrls { get; init; }
    public string? FilePath { get; init; }
}

public record StartHlsEgressRequest
{
    /// <summary>
    /// The name of the HLS playlist file (default: "playlist.m3u8")
    /// </summary>
    public string? PlaylistName { get; init; }

    /// <summary>
    /// Duration of each segment in seconds (default: 6)
    /// </summary>
    public uint SegmentDuration { get; init; } = 6;

    /// <summary>
    /// Number of segments to keep (0 = unlimited, keep all)
    /// </summary>
    public int SegmentCount { get; init; } = 0;

    /// <summary>
    /// Layout for video composition (default: "default")
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// Base URL for HLS playback (default: "https://{host}/hls")
    /// </summary>
    public string? HlsBaseUrl { get; init; }
}

public record SendChatMessageRequest
{
    public required string Content { get; init; }
}

public record TimeoutRequest
{
    public int? DurationMinutes { get; init; }
}