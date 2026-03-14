using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Slug), IsUnique = true)]
public class SnRealm : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Slug { get; set; } = string.Empty;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public SnVerificationMark? Verification { get; set; }

    [IgnoreMember] [JsonIgnore] public List<SnRealmMember> Members { get; set; } = new List<SnRealmMember>();
    [IgnoreMember] [JsonIgnore] public List<SnRealmLabel> Labels { get; set; } = new List<SnRealmLabel>();

    public Guid AccountId { get; set; }
    public decimal BoostPoints { get; set; }

    [NotMapped]
    public int BoostLevel => RealmBoostPolicy.GetBoostLevel(BoostPoints);

    public string ResourceIdentifier => $"realm:{Id}";

    public DyRealm ToProtoValue()
    {
        return new DyRealm
        {
            Id = Id.ToString(),
            Name = Name,
            Slug = Slug,
            Description = Description,
            Picture = Picture?.ToProtoValue(),
            Background = Background?.ToProtoValue(),
            Verification = Verification?.ToProtoValue(),
            IsCommunity = IsCommunity,
            AccountId = AccountId.ToString(),
            IsPublic = IsPublic,
            BoostPoints = BoostPoints.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoostLevel = BoostLevel
        };
    }

    public static SnRealm FromProtoValue(DyRealm proto)
    {
        return new SnRealm
        {
            Id = Guid.Parse(proto.Id),
            Name = proto.Name,
            Slug = proto.Slug,
            Description = proto.Description,
            Picture = proto.Picture is not null ? SnCloudFileReferenceObject.FromProtoValue(proto.Picture) : null,
            Background = proto.Background is not null
                ? SnCloudFileReferenceObject.FromProtoValue(proto.Background)
                : null,
            Verification =
                proto.Verification is not null ? SnVerificationMark.FromProtoValue(proto.Verification) : null,
            IsCommunity = proto.IsCommunity,
            IsPublic = proto.IsPublic,
            AccountId = Guid.Parse(proto.AccountId),
            BoostPoints = string.IsNullOrWhiteSpace(proto.BoostPoints)
                ? 0
                : decimal.Parse(proto.BoostPoints, System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}

public static class RealmBoostPolicy
{
    public const decimal SharePoints = 100m;
    public const decimal Level1Points = 1000m;
    public const decimal Level2Points = 5000m;
    public const decimal Level3Points = 15000m;
    public const int ExpirationDays = 30;

    public static Instant GetActiveCutoff(Instant now) => now - Duration.FromDays(ExpirationDays);

    public static int GetBoostLevel(decimal boostPoints) => boostPoints switch
    {
        >= Level3Points => 3,
        >= Level2Points => 2,
        >= Level1Points => 1,
        _ => 0
    };

    public static int GetLabelCap(int boostLevel) => boostLevel switch
    {
        >= 3 => 200,
        >= 2 => 50,
        >= 1 => 10,
        _ => 0
    };
}

public abstract class RealmMemberRole
{
    public const int Owner = 100;
    public const int Moderator = 50;
    public const int Normal = 0;
}

public class SnRealmMember : ModelBase
{
    public Guid RealmId { get; set; }
    public SnRealm Realm { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
    [NotMapped] public SnAccountStatus? Status { get; set; }
    [MaxLength(1024)] public string? Nick { get; set; }
    [MaxLength(4096)] public string? Bio { get; set; }
    public Guid? LabelId { get; set; }
    public SnRealmLabel? Label { get; set; }
    public int Experience { get; set; }

    [NotMapped] public int Level => Leveling.GetLevelFromExp(Experience);
    [NotMapped] public double LevelingProgress => Leveling.GetProgressToNextLevel(Experience);

    public int Role { get; set; } = RealmMemberRole.Normal;
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }

    public DyRealmMember ToProtoValue()
    {
        var proto = new DyRealmMember
        {
            AccountId = AccountId.ToString(),
            RealmId = RealmId.ToString(),
            Role = Role,
            Nick = Nick ?? string.Empty,
            Bio = Bio ?? string.Empty,
            Experience = Experience,
            Level = Level,
            LevelingProgress = LevelingProgress,
            JoinedAt = JoinedAt?.ToTimestamp(),
            LeaveAt = LeaveAt?.ToTimestamp(),
            LabelId = LabelId?.ToString() ?? string.Empty
        };
        if (Realm != null)
        {
            proto.Realm = Realm.ToProtoValue();
        }
        if (Account != null)
        {
            proto.Account = Account.ToProtoValue();
        }
        if (Label != null)
        {
            proto.LabelName = Label.Name;
            proto.LabelDescription = Label.Description ?? string.Empty;
            proto.LabelColor = Label.Color ?? string.Empty;
            proto.LabelIcon = Label.Icon ?? string.Empty;
        }

        return proto;
    }

    public static SnRealmMember FromProtoValue(DyRealmMember proto)
    {
        var member = new SnRealmMember
        {
            AccountId = Guid.Parse(proto.AccountId),
            RealmId = Guid.Parse(proto.RealmId),
            Role = proto.Role,
            Nick = string.IsNullOrWhiteSpace(proto.Nick) ? null : proto.Nick,
            Bio = string.IsNullOrWhiteSpace(proto.Bio) ? null : proto.Bio,
            Experience = proto.Experience,
            JoinedAt = proto.JoinedAt?.ToInstant(),
            LeaveAt = proto.LeaveAt?.ToInstant(),
            LabelId = Guid.TryParse(proto.LabelId, out var labelId) ? labelId : null,
            Realm = proto.Realm != null
                ? SnRealm.FromProtoValue(proto.Realm)
                : new SnRealm() // Provide default or handle null
        };
        if (proto.Account != null)
        {
            member.Account = SnAccount.FromProtoValue(proto.Account);
        }
        if (!string.IsNullOrWhiteSpace(proto.LabelName))
        {
            member.Label = new SnRealmLabel
            {
                Id = member.LabelId ?? Guid.Empty,
                RealmId = member.RealmId,
                Name = proto.LabelName,
                Description = string.IsNullOrWhiteSpace(proto.LabelDescription) ? null : proto.LabelDescription,
                Color = string.IsNullOrWhiteSpace(proto.LabelColor) ? null : proto.LabelColor,
                Icon = string.IsNullOrWhiteSpace(proto.LabelIcon) ? null : proto.LabelIcon,
            };
        }

        return member;
    }
}

public class SnRealmLabel : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    [JsonIgnore] public SnRealm Realm { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Description { get; set; }
    [MaxLength(64)] public string? Color { get; set; }
    [MaxLength(256)] public string? Icon { get; set; }
    public Guid CreatedByAccountId { get; set; }

    public object ToDisplayPayload() => new
    {
        id = Id,
        realm_id = RealmId,
        name = Name,
        description = Description,
        color = Color,
        icon = Icon,
        created_by_account_id = CreatedByAccountId,
    };
}

public class SnRealmBoostContribution : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    public Guid AccountId { get; set; }
    [MaxLength(128)] public string Currency { get; set; } = "points";
    public decimal Amount { get; set; }
    public Guid OrderId { get; set; }
    public Guid TransactionId { get; set; }

    [NotMapped]
    public decimal Shares => Amount / RealmBoostPolicy.SharePoints;

    [NotMapped]
    public Instant ExpiresAt => CreatedAt + Duration.FromDays(RealmBoostPolicy.ExpirationDays);
}

public class SnRealmExperienceRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RealmId { get; set; }
    public Guid AccountId { get; set; }
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public int Delta { get; set; }
}
