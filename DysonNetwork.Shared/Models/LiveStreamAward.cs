using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using Duration = NodaTime.Duration;
using LiveStreamAwardAttitude = DysonNetwork.Shared.Models.LiveStreamAwardAttitude;

namespace DysonNetwork.Shared.Models;

public class SnLiveStreamAward : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public decimal Amount { get; set; }

    public LiveStreamAwardAttitude Attitude { get; set; }

    [MaxLength(4096)]
    public string? Message { get; set; }

    public Guid LiveStreamId { get; set; }

    [JsonIgnore]
    public SnLiveStream LiveStream { get; set; } = null!;

    public Guid AccountId { get; set; }

    [MaxLength(128)]
    public string SenderName { get; set; } = null!;

    [NotMapped]
    [JsonIgnore]
    public int HighlightDurationSeconds => (int)(Amount * 2);

    [NotMapped]
    [JsonIgnore]
    public Instant? HighlightedUntil => CreatedAt.Plus(Duration.FromSeconds(HighlightDurationSeconds));

    public LiveStreamAward ToProtoValue()
    {
        var proto = new LiveStreamAward
        {
            Id = Id.ToString(),
            Amount = (double)Amount,
            Attitude = (Proto.LiveStreamAwardAttitude)((int)Attitude + 1),
            LiveStreamId = LiveStreamId.ToString(),
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset()),
        };
        if (Message != null)
            proto.Message = Message;
        return proto;
    }
}
