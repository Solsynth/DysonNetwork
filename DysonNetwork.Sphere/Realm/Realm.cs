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
    [MaxLength(4096)] public string? VerifiedAs { get; set; }
    public Instant? VerifiedAt { get; set; }
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [JsonIgnore] public ICollection<RealmMember> Members { get; set; } = new List<RealmMember>();
    [JsonIgnore] public ICollection<ChatRoom> ChatRooms { get; set; } = new List<ChatRoom>();

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;

    public string ResourceIdentifier => $"realm/{Id}";
}

public enum RealmMemberRole
{
    Owner = 100,
    Moderator = 50,
    Normal = 0
}

public class RealmMember : ModelBase
{
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    public RealmMemberRole Role { get; set; } = RealmMemberRole.Normal;
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }
}