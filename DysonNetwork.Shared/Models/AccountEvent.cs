using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum StatusAttitude
{
    Positive,
    Negative,
    Neutral,
}

public enum StatusType
{
    Default,
    Busy,
    DoNotDisturb,
    Invisible,
}

public class SnAccountStatus : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }

    [NotMapped]
    public bool IsOnline { get; set; }

    [NotMapped]
    public bool IsCustomized { get; set; } = true;
    public StatusType Type { get; set; } = StatusType.Default;

    [MaxLength(1024)]
    public string? Label { get; set; }

    [MaxLength(128)]
    public string? Symbol { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }
    public Instant? ClearedAt { get; set; }

    [MaxLength(4096)]
    public string? AppIdentifier { get; set; }

    /// <summary>
    /// Indicates this status is created based on running process or rich presence
    /// </summary>
    public bool IsAutomated { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public DyAccountStatus ToProtoValue()
    {
        var proto = new DyAccountStatus
        {
            Id = Id.ToString(),
            Attitude = Attitude switch
            {
                StatusAttitude.Positive => DyStatusAttitude.DyPositive,
                StatusAttitude.Negative => DyStatusAttitude.DyNegative,
                StatusAttitude.Neutral => DyStatusAttitude.DyNeutral,
                _ => DyStatusAttitude.Unspecified,
            },
            IsOnline = IsOnline,
            IsCustomized = IsCustomized,
            IsInvisible = Type == StatusType.Invisible,
            IsNotDisturb = Type == StatusType.DoNotDisturb,
            Label = Label ?? string.Empty,
            Type = (DyAccountStatusType)Type,
            Symbol = Symbol ?? string.Empty,
            Meta = InfraObjectCoder.ConvertObjectToByteString(Meta),
            ClearedAt = ClearedAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            AppIdentifier = AppIdentifier ?? string.Empty,
            IsAutomated = IsAutomated,
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp(),
            DeletedAt = DeletedAt?.ToTimestamp(),
        };

        return proto;
    }

    public static SnAccountStatus FromProtoValue(DyAccountStatus proto)
    {
        var status = new SnAccountStatus
        {
            Id = Guid.Parse(proto.Id),
            Attitude = proto.Attitude switch
            {
                DyStatusAttitude.DyPositive => StatusAttitude.Positive,
                DyStatusAttitude.DyNegative => StatusAttitude.Negative,
                _ => StatusAttitude.Neutral,
            },
            IsOnline = proto.IsOnline,
            IsCustomized = proto.IsCustomized,
            Type = proto.Type != DyAccountStatusType.Default
                ? Enum.IsDefined(typeof(StatusType), (int)proto.Type) ? (StatusType)proto.Type : StatusType.Default
                : proto.IsInvisible
                    ? StatusType.Invisible
                    : proto.IsNotDisturb
                        ? StatusType.DoNotDisturb
                        : StatusType.Default,
            Label = proto.Label,
            Symbol = string.IsNullOrWhiteSpace(proto.Symbol) ? null : proto.Symbol,
            Meta = InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta),
            ClearedAt = proto.ClearedAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId),
            AppIdentifier = string.IsNullOrWhiteSpace(proto.AppIdentifier) ? null : proto.AppIdentifier,
            IsAutomated = proto.IsAutomated,
            CreatedAt = proto.CreatedAt?.ToInstant() ?? default,
            UpdatedAt = proto.UpdatedAt?.ToInstant() ?? default,
            DeletedAt = proto.DeletedAt?.ToInstant(),
        };

        return status;
    }
}

public enum CheckInResultLevel
{
    Worst,
    Worse,
    Normal,
    Better,
    Best,
    Special,
}

public class SnCheckInResult : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CheckInResultLevel Level { get; set; }
    public decimal? RewardPoints { get; set; }
    public int? RewardExperience { get; set; }

    [Column(TypeName = "jsonb")]
    public List<CheckInFortuneTip> Tips { get; set; } = new List<CheckInFortuneTip>();

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public Instant? BackdatedFrom { get; set; }
}

public class CheckInFortuneTip
{
    public bool IsPositive { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
}

/// <summary>
/// This method should not be mapped. Used to generate the daily event calendar.
/// </summary>
public class DailyEventResponse
{
    public Instant Date { get; set; }
    public SnCheckInResult? CheckInResult { get; set; }
    public List<SnAccountStatus> Statuses { get; set; } = new List<SnAccountStatus>();
}

public enum PresenceType
{
    Unknown,
    Gaming,
    Music,
    Workout,
}

public class SnPresenceActivity : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PresenceType Type { get; set; } = PresenceType.Unknown;

    [MaxLength(4096)]
    public string? ManualId { get; set; }

    [MaxLength(4096)]
    public string? Title { get; set; }

    [MaxLength(4096)]
    public string? Subtitle { get; set; }

    [MaxLength(4096)]
    public string? Caption { get; set; }

    [MaxLength(4096)]
    public string? LargeImage { get; set; }

    [MaxLength(4096)]
    public string? SmallImage { get; set; }

    [MaxLength(4096)]
    public string? TitleUrl { get; set; }

    [MaxLength(4096)]
    public string? SubtitleUrl { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }

    public int LeaseMinutes { get; set; } = 5; // Lease period in minutes (1-60)
    public Instant LeaseExpiresAt { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;
}

public enum TimelineEventType
{
    StatusChange,
    Activity,
}

public class AccountTimelineItem
{
    public Guid Id { get; set; }
    public Instant CreatedAt { get; set; }
    public TimelineEventType EventType { get; set; }
    public SnAccountStatus? Status { get; set; }
    public SnPresenceActivity? Activity { get; set; }
}
