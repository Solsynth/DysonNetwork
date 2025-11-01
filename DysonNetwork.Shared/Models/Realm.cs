using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
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

    [JsonIgnore] public ICollection<SnRealmMember> Members { get; set; } = new List<SnRealmMember>();

    public Guid AccountId { get; set; }

    public string ResourceIdentifier => $"realm:{Id}";

    public Realm ToProtoValue()
    {
        return new Realm
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
            IsPublic = IsPublic
        };
    }

    public static SnRealm FromProtoValue(Realm proto)
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
        };
    }
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

    public int Role { get; set; } = RealmMemberRole.Normal;
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }

    public Proto.RealmMember ToProtoValue()
    {
        var proto = new Proto.RealmMember
        {
            AccountId = AccountId.ToString(),
            RealmId = RealmId.ToString(),
            Role = Role,
            JoinedAt = JoinedAt?.ToTimestamp(),
            LeaveAt = LeaveAt?.ToTimestamp(),
            Realm = Realm.ToProtoValue()
        };
        if (Account != null)
        {
            proto.Account = Account.ToProtoValue();
        }

        return proto;
    }

    public static SnRealmMember FromProtoValue(RealmMember proto)
    {
        var member = new SnRealmMember
        {
            AccountId = Guid.Parse(proto.AccountId),
            RealmId = Guid.Parse(proto.RealmId),
            Role = proto.Role,
            JoinedAt = proto.JoinedAt?.ToInstant(),
            LeaveAt = proto.LeaveAt?.ToInstant(),
            Realm = proto.Realm != null
                ? SnRealm.FromProtoValue(proto.Realm)
                : new SnRealm() // Provide default or handle null
        };
        if (proto.Account != null)
        {
            member.Account = SnAccount.FromProtoValue(proto.Account);
        }

        return member;
    }
}