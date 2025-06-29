using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

public enum PublisherType
{
    Individual,
    Organizational
}

[Index(nameof(Name), IsUnique = true)]
public class Publisher : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    public PublisherType Type { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public Account.VerificationMark? Verification { get; set; }

    [JsonIgnore] public ICollection<Post.Post> Posts { get; set; } = new List<Post.Post>();
    [JsonIgnore] public ICollection<PostCollection> Collections { get; set; } = new List<PostCollection>();
    [JsonIgnore] public ICollection<PublisherMember> Members { get; set; } = new List<PublisherMember>();
    [JsonIgnore] public ICollection<PublisherFeature> Features { get; set; } = new List<PublisherFeature>();

    [JsonIgnore]
    public ICollection<PublisherSubscription> Subscriptions { get; set; } = new List<PublisherSubscription>();

    public Guid? AccountId { get; set; }
    public Account.Account? Account { get; set; }
    public Guid? RealmId { get; set; }
    [JsonIgnore] public Realm.Realm? Realm { get; set; }

    public string ResourceIdentifier => $"publisher/{Id}";
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
    public Account.Account Account { get; set; } = null!;

    public PublisherMemberRole Role { get; set; } = PublisherMemberRole.Viewer;
    public Instant? JoinedAt { get; set; }
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
    [JsonIgnore] public Account.Account Account { get; set; } = null!;

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