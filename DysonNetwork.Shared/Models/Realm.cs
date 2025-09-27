using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

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
    
    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }
    
    [Column(TypeName = "jsonb")] public SnVerificationMark? Verification { get; set; }

    [JsonIgnore] public ICollection<SnRealmMember> Members { get; set; } = new List<SnRealmMember>();
    [JsonIgnore] public ICollection<SnChatRoom> ChatRooms { get; set; } = new List<SnChatRoom>();

    public Guid AccountId { get; set; }

    public string ResourceIdentifier => $"realm:{Id}";
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
}