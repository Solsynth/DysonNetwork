using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Realm;

[Index(nameof(Slug), IsUnique = true)]
public class Realm : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Slug { get; set; } = string.Empty;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }
    
    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }
    
    [Column(TypeName = "jsonb")] public Account.VerificationMark? Verification { get; set; }

    [JsonIgnore] public ICollection<RealmMember> Members { get; set; } = new List<RealmMember>();
    [JsonIgnore] public ICollection<ChatRoom> ChatRooms { get; set; } = new List<ChatRoom>();
    [JsonIgnore] public ICollection<RealmTag> RealmTags { get; set; } = new List<RealmTag>();

    public Guid AccountId { get; set; }

    public string ResourceIdentifier => $"realm:{Id}";
}

public abstract class RealmMemberRole
{
    public const int Owner = 100;
    public const int Moderator = 50;
    public const int Normal = 0;
}

public class RealmMember : ModelBase
{
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int Role { get; set; } = RealmMemberRole.Normal;
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }
}