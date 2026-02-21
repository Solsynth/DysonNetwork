using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum LiveStreamStatus
{
    Pending,
    Active,
    Ended,
    Error,
}

public enum LiveStreamType
{
    Regular,
    Interactive,
}

public enum LiveStreamVisibility
{
    Public,
    Unlisted,
    Private,
}

public class SnLiveStream : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string? Title { get; set; }

    [MaxLength(4096)]
    public string? Description { get; set; }

    [MaxLength(128)]
    public string? Slug { get; set; }

    public LiveStreamType Type { get; set; } = LiveStreamType.Regular;
    public LiveStreamVisibility Visibility { get; set; } = LiveStreamVisibility.Public;
    public LiveStreamStatus Status { get; set; } = LiveStreamStatus.Pending;

    [MaxLength(256)]
    public string RoomName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? IngressId { get; set; }

    [MaxLength(256)]
    public string? IngressStreamKey { get; set; }

    [MaxLength(256)]
    public string? EgressId { get; set; }

    [MaxLength(256)]
    public string? HlsEgressId { get; set; }

    [MaxLength(512)]
    public string? HlsPlaylistPath { get; set; }

    public Instant? HlsStartedAt { get; set; }

    public Instant? StartedAt { get; set; }
    public Instant? EndedAt { get; set; }

    public long TotalDurationSeconds { get; set; }

    [NotMapped]
    public Duration? Duration
    {
        get
        {
            if (!StartedAt.HasValue || !EndedAt.HasValue)
                return null;
            return EndedAt.Value - StartedAt.Value;
        }
    }

    [NotMapped]
    [JsonIgnore]
    public Duration? TotalDuration
    {
        get
        {
            var currentDuration = Duration;
            if (currentDuration == null)
                return Duration.FromSeconds(TotalDurationSeconds);
            return currentDuration + Duration.FromSeconds(TotalDurationSeconds);
        }
    }

    [NotMapped]
    [JsonIgnore]
    public string? DurationFormatted
    {
        get
        {
            var totalDur = TotalDuration;
            if (!totalDur.HasValue)
                return null;
            var d = totalDur.Value;
            if (d.TotalHours >= 1)
                return $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}";
            return $"{d.Minutes}:{d.Seconds:D2}";
        }
    }

    public int ViewerCount { get; set; }
    public int PeakViewerCount { get; set; }

    public decimal TotalAwardScore { get; set; }

    [Column(TypeName = "jsonb")]
    public SnCloudFileReferenceObject? Thumbnail { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }

    public Guid? PublisherId { get; set; }
    public SnPublisher? Publisher { get; set; }

    public string ResourceIdentifier => $"livestream:{Id}";

    public LiveStream ToProtoValue()
    {
        var proto = new LiveStream
        {
            Id = Id.ToString(),
            Title = Title ?? string.Empty,
            Description = Description ?? string.Empty,
            Slug = Slug ?? string.Empty,
            Type = (Proto.LiveStreamType)((int)Type + 1),
            Visibility = (Proto.LiveStreamVisibility)((int)Visibility + 1),
            Status = (Proto.LiveStreamStatus)((int)Status + 1),
            RoomName = RoomName,
            ViewerCount = ViewerCount,
            PeakViewerCount = PeakViewerCount,
            TotalAwardScore = (double)TotalAwardScore,
            PublisherId = PublisherId.ToString(),
            Publisher = Publisher?.ToProtoValue(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset()),
        };

        if (!string.IsNullOrEmpty(IngressId))
            proto.IngressId = IngressId;

        if (!string.IsNullOrEmpty(IngressStreamKey))
            proto.IngressStreamKey = IngressStreamKey;

        if (!string.IsNullOrEmpty(EgressId))
            proto.EgressId = EgressId;

        if (!string.IsNullOrEmpty(HlsEgressId))
            proto.HlsEgressId = HlsEgressId;

        if (!string.IsNullOrEmpty(HlsPlaylistPath))
            proto.HlsPlaylistUrl = HlsPlaylistPath;

        if (HlsStartedAt.HasValue)
            proto.HlsStartedAt = Timestamp.FromDateTimeOffset(HlsStartedAt.Value.ToDateTimeOffset());

        if (StartedAt.HasValue)
            proto.StartedAt = Timestamp.FromDateTimeOffset(StartedAt.Value.ToDateTimeOffset());

        if (EndedAt.HasValue)
            proto.EndedAt = Timestamp.FromDateTimeOffset(EndedAt.Value.ToDateTimeOffset());

        if (Duration.HasValue)
            proto.DurationSeconds = (long)Duration.Value.TotalSeconds;

        if (TotalDurationSeconds > 0)
            proto.TotalDurationSeconds = TotalDurationSeconds;

        if (Thumbnail != null)
            proto.Thumbnail = Thumbnail.ToProtoValue();

        if (Metadata != null)
            proto.Metadata = InfraObjectCoder.ConvertObjectToByteString(Metadata);

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnLiveStream FromProtoValue(LiveStream proto)
    {
        var liveStream = new SnLiveStream
        {
            Id = Guid.Parse(proto.Id),
            Title = string.IsNullOrEmpty(proto.Title) ? null : proto.Title,
            Description = string.IsNullOrEmpty(proto.Description) ? null : proto.Description,
            Slug = string.IsNullOrEmpty(proto.Slug) ? null : proto.Slug,
            Type = (LiveStreamType)((int)proto.Type - 1),
            Visibility = (LiveStreamVisibility)((int)proto.Visibility - 1),
            Status = (LiveStreamStatus)((int)proto.Status - 1),
            RoomName = proto.RoomName,
            ViewerCount = proto.ViewerCount,
            PeakViewerCount = proto.PeakViewerCount,
            PublisherId = Guid.Parse(proto.PublisherId),
            Publisher = proto.Publisher != null ? SnPublisher.FromProtoValue(proto.Publisher) : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
        };

        if (!string.IsNullOrEmpty(proto.IngressId))
            liveStream.IngressId = proto.IngressId;

        if (!string.IsNullOrEmpty(proto.IngressStreamKey))
            liveStream.IngressStreamKey = proto.IngressStreamKey;

        if (!string.IsNullOrEmpty(proto.EgressId))
            liveStream.EgressId = proto.EgressId;

        if (!string.IsNullOrEmpty(proto.HlsEgressId))
            liveStream.HlsEgressId = proto.HlsEgressId;

        if (!string.IsNullOrEmpty(proto.HlsPlaylistUrl))
            liveStream.HlsPlaylistPath = proto.HlsPlaylistUrl;

        if (proto.HlsStartedAt != null)
            liveStream.HlsStartedAt = Instant.FromDateTimeOffset(proto.HlsStartedAt.ToDateTimeOffset());

        if (proto.StartedAt != null)
            liveStream.StartedAt = Instant.FromDateTimeOffset(proto.StartedAt.ToDateTimeOffset());

        if (proto.EndedAt != null)
            liveStream.EndedAt = Instant.FromDateTimeOffset(proto.EndedAt.ToDateTimeOffset());

        if (proto.Thumbnail != null)
            liveStream.Thumbnail = SnCloudFileReferenceObject.FromProtoValue(proto.Thumbnail);

        if (proto.Metadata != null)
            liveStream.Metadata = InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object>>(proto.Metadata);

        if (proto.DeletedAt != null)
            liveStream.DeletedAt = Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset());

        return liveStream;
    }
}
