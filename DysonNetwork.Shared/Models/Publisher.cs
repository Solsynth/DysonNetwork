using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum PublisherType
{
    Individual,
    Organizational
}

[Index(nameof(Name), IsUnique = true)]
public class SnPublisher : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PublisherType Type { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public SnVerificationMark? Verification { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }

    [MaxLength(8192)] [JsonIgnore] public string? PrivateKeyPem { get; set; }
    [MaxLength(8192)] public string? PublicKeyPem { get; set; }

    [IgnoreMember] [JsonIgnore] public List<SnPost> Posts { get; set; } = [];
    [IgnoreMember] [JsonIgnore] public List<SnPoll> Polls { get; set; } = [];
    [IgnoreMember] [JsonIgnore] public List<SnPostCollection> Collections { get; set; } = [];
    [IgnoreMember] [JsonIgnore] public List<SnPublisherMember> Members { get; set; } = [];
    [IgnoreMember] [JsonIgnore] public List<SnPublisherFeature> Features { get; set; } = [];

    [JsonIgnore]
    public List<SnPublisherSubscription> Subscriptions { get; set; } = [];

    public Guid? AccountId { get; set; }
    public Guid? RealmId { get; set; }
    [NotMapped] public SnRealm? Realm { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }

    public string ResourceIdentifier => $"publisher:{Id}";

    public static SnPublisher FromProtoValue(DyPublisher proto)
    {
        var publisher = new SnPublisher
        {
            Id = Guid.TryParse(proto.Id, out var id) ? id : Guid.NewGuid(),
            Type = proto.Type == DyPublisherType.DyPubIndividual
                ? PublisherType.Individual
                : PublisherType.Organizational,
            Name = proto.Name,
            Nick = proto.Nick,
            Bio = proto.Bio,
            AccountId = Guid.TryParse(proto.AccountId, out var accountId) ? accountId : null,
            RealmId = Guid.TryParse(proto.RealmId, out var realmId) ? realmId : null,
        };

        if (proto.Picture != null)
        {
            publisher.Picture = new SnCloudFileReferenceObject
            {
                Id = proto.Picture.Id,
                Name = proto.Picture.Name,
                MimeType = proto.Picture.MimeType,
                Hash = proto.Picture.Hash,
                Size = proto.Picture.Size,
            };
        }

        if (proto.Background != null)
        {
            publisher.Background = new SnCloudFileReferenceObject
            {
                Id = proto.Background.Id,
                Name = proto.Background.Name,
                MimeType = proto.Background.MimeType,
                Hash = proto.Background.Hash,
                Size = proto.Background.Size,
            };
        }

        return publisher;
    }

    public DyPublisher ToProtoValue()
    {
        var p = new DyPublisher
        {
            Id = Id.ToString(),
            Type = Type == PublisherType.Individual
                ? DyPublisherType.DyPubIndividual
                : DyPublisherType.DyPubOrganizational,
            Name = Name,
            Nick = Nick,
            Bio = Bio,
            AccountId = AccountId?.ToString() ?? string.Empty,
            RealmId = RealmId?.ToString() ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
        if (Picture is not null)
        {
            p.Picture = new DyCloudFile
            {
                Id = Picture.Id,
                Name = Picture.Name,
                MimeType = Picture.MimeType,
                Hash = Picture.Hash,
                Size = Picture.Size,
            };
        }

        if (Background is not null)
        {
            p.Background = new DyCloudFile
            {
                Id = Background.Id,
                Name = Background.Name,
                MimeType = Background.MimeType,
                Hash = Background.Hash,
                Size = Background.Size,
            };
        }

        return p;
    }
}

public enum PublisherMemberRole
{
    Owner = 100,
    Manager = 75,
    Editor = 50,
    Viewer = 25
}

public class SnPublisherMember : ModelBase
{
    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }

    public PublisherMemberRole Role { get; set; } = PublisherMemberRole.Viewer;
    public Instant? JoinedAt { get; set; }
    
    
    public DyPublisherMember ToProto()
    {
        return new DyPublisherMember
        {
            PublisherId = PublisherId.ToString(),
            AccountId = AccountId.ToString(),
            Role = Role switch
            {
                PublisherMemberRole.Owner => DyPublisherMemberRole.DyOwner,
                PublisherMemberRole.Manager => DyPublisherMemberRole.DyManager,
                PublisherMemberRole.Editor => DyPublisherMemberRole.DyEditor,
                PublisherMemberRole.Viewer => DyPublisherMemberRole.DyViewer,
                _ => throw new ArgumentOutOfRangeException(nameof(Role), Role, null)
            },
            JoinedAt = JoinedAt?.ToTimestamp()
        };
    }

    public static SnPublisherMember FromProtoValue(DyPublisherMember proto)
    {
        return new SnPublisherMember
        {
            PublisherId = Guid.Parse(proto.PublisherId),
            AccountId = Guid.Parse(proto.AccountId),
            Role = proto.Role switch
            {
                DyPublisherMemberRole.DyOwner => PublisherMemberRole.Owner,
                DyPublisherMemberRole.DyManager => PublisherMemberRole.Manager,
                DyPublisherMemberRole.DyEditor => PublisherMemberRole.Editor,
                _ => PublisherMemberRole.Viewer
            },
            JoinedAt = proto.JoinedAt?.ToInstant(),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };
    }
}

public class SnPublisherSubscription : ModelBase
{
    public Guid Id { get; set; }

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }
}

public class SnPublisherFeature : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Flag { get; set; } = null!;
    public Instant? ExpiredAt { get; set; }

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
}

public abstract class PublisherFeatureFlag
{
    public static List<string> AllFlags => [Develop];
    public static string Develop = "develop";
}
