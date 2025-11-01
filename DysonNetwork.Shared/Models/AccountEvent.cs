using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

public class SnAccountStatus : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }

    [NotMapped]
    public bool IsOnline { get; set; }

    [NotMapped]
    public bool IsCustomized { get; set; } = true;
    public bool IsInvisible { get; set; }
    public bool IsNotDisturb { get; set; }

    [MaxLength(1024)]
    public string? Label { get; set; }

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
    public SnAccount Account { get; set; } = null!;

    public AccountStatus ToProtoValue()
    {
        var proto = new AccountStatus
        {
            Id = Id.ToString(),
            Attitude = Attitude switch
            {
                StatusAttitude.Positive => Shared.Proto.StatusAttitude.Positive,
                StatusAttitude.Negative => Shared.Proto.StatusAttitude.Negative,
                StatusAttitude.Neutral => Shared.Proto.StatusAttitude.Neutral,
                _ => Shared.Proto.StatusAttitude.Unspecified,
            },
            IsOnline = IsOnline,
            IsCustomized = IsCustomized,
            IsInvisible = IsInvisible,
            IsNotDisturb = IsNotDisturb,
            Label = Label ?? string.Empty,
            Meta = GrpcTypeHelper.ConvertObjectToByteString(Meta),
            ClearedAt = ClearedAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
        };

        return proto;
    }

    public static SnAccountStatus FromProtoValue(AccountStatus proto)
    {
        var status = new SnAccountStatus
        {
            Id = Guid.Parse(proto.Id),
            Attitude = proto.Attitude switch
            {
                Shared.Proto.StatusAttitude.Positive => StatusAttitude.Positive,
                Shared.Proto.StatusAttitude.Negative => StatusAttitude.Negative,
                Shared.Proto.StatusAttitude.Neutral => StatusAttitude.Neutral,
                _ => StatusAttitude.Neutral,
            },
            IsOnline = proto.IsOnline,
            IsCustomized = proto.IsCustomized,
            IsInvisible = proto.IsInvisible,
            IsNotDisturb = proto.IsNotDisturb,
            Label = proto.Label,
            Meta = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta),
            ClearedAt = proto.ClearedAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId),
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
    public ICollection<CheckInFortuneTip> Tips { get; set; } = new List<CheckInFortuneTip>();

    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;

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
    public ICollection<SnAccountStatus> Statuses { get; set; } = new List<SnAccountStatus>();
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

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Meta { get; set; }

    public int LeaseMinutes { get; set; } = 5; // Lease period in minutes (1-60)
    public Instant LeaseExpiresAt { get; set; }

    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
}
