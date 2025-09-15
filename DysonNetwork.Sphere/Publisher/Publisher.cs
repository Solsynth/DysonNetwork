using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using VerificationMark = DysonNetwork.Shared.Data.VerificationMark;
using Account = DysonNetwork.Shared.Data.AccountReference;

namespace DysonNetwork.Sphere.Publisher;

public enum PublisherType
{
    Individual,
    Organizational
}

[Index(nameof(Name), IsUnique = true)]
public class Publisher : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PublisherType Type { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public VerificationMark? Verification { get; set; }

    [JsonIgnore] public ICollection<Post.Post> Posts { get; set; } = new List<Post.Post>();
    [JsonIgnore] public ICollection<Poll.Poll> Polls { get; set; } = new List<Poll.Poll>();
    [JsonIgnore] public ICollection<PostCollection> Collections { get; set; } = new List<PostCollection>();
    [JsonIgnore] public ICollection<PublisherMember> Members { get; set; } = new List<PublisherMember>();
    [JsonIgnore] public ICollection<PublisherFeature> Features { get; set; } = new List<PublisherFeature>();

    [JsonIgnore]
    public ICollection<PublisherSubscription> Subscriptions { get; set; } = new List<PublisherSubscription>();

    public Guid? AccountId { get; set; }
    public Guid? RealmId { get; set; }
    [JsonIgnore] public Realm.Realm? Realm { get; set; }
    [NotMapped] public Account? Account { get; set; }

    public string ResourceIdentifier => $"publisher:{Id}";

    public Shared.Proto.Publisher ToProto(AppDatabase db)
    {
        var p = new Shared.Proto.Publisher()
        {
            Id = Id.ToString(),
            Type = Type == PublisherType.Individual
                ? Shared.Proto.PublisherType.PubIndividual
                : Shared.Proto.PublisherType.PubOrganizational,
            Name = Name,
            Nick = Nick,
            Bio = Bio,
            AccountId = AccountId?.ToString() ?? string.Empty,
            RealmId = RealmId?.ToString() ?? string.Empty,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
        if (Picture is not null)
        {
            p.Picture = new Shared.Proto.CloudFile
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
            p.Background = new Shared.Proto.CloudFile
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

public class PublisherMember : ModelBase
{
    public Guid PublisherId { get; set; }
    [JsonIgnore] public Publisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public Account? Account { get; set; }

    public PublisherMemberRole Role { get; set; } = PublisherMemberRole.Viewer;
    public Instant? JoinedAt { get; set; }
    
    
    public Shared.Proto.PublisherMember ToProto()
    {
        return new Shared.Proto.PublisherMember()
        {
            PublisherId = PublisherId.ToString(),
            AccountId = AccountId.ToString(),
            Role = Role switch
            {
                PublisherMemberRole.Owner => Shared.Proto.PublisherMemberRole.Owner,
                PublisherMemberRole.Manager => Shared.Proto.PublisherMemberRole.Manager,
                PublisherMemberRole.Editor => Shared.Proto.PublisherMemberRole.Editor,
                PublisherMemberRole.Viewer => Shared.Proto.PublisherMemberRole.Viewer,
                _ => throw new ArgumentOutOfRangeException(nameof(Role), Role, null)
            },
            JoinedAt = JoinedAt?.ToTimestamp()
        };
    }
}

public enum PublisherSubscriptionStatus
{
    Active,
    Expired,
    Cancelled
}

public class PublisherSubscription : ModelBase
{
    public Guid Id { get; set; }

    public Guid PublisherId { get; set; }
    [JsonIgnore] public Publisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }

    public PublisherSubscriptionStatus Status { get; set; } = PublisherSubscriptionStatus.Active;
    public int Tier { get; set; } = 0;
}

public class PublisherFeature : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Flag { get; set; } = null!;
    public Instant? ExpiredAt { get; set; }

    public Guid PublisherId { get; set; }
    public Publisher Publisher { get; set; } = null!;
}

public abstract class PublisherFeatureFlag
{
    public static List<string> AllFlags => [Develop];
    public static string Develop = "develop";
}