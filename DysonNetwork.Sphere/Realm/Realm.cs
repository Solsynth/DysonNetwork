using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Realm;

[Index(nameof(Slug), IsUnique = true)]
public class Realm : ModelBase
{
    public long Id { get; set; }
    [MaxLength(1024)] public string Slug { get; set; } = string.Empty;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    [MaxLength(4096)] public string? VerifiedAs { get; set; }
    public Instant? VerifiedAt { get; set; }
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }

    public CloudFile? Picture { get; set; }
    public CloudFile? Background { get; set; }
    
    [JsonIgnore] public ICollection<RealmMember> Members { get; set; } = new List<RealmMember>();
    [JsonIgnore] public ICollection<ChatRoom> ChatRooms { get; set; } = new List<ChatRoom>();

    public long AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
}

public enum RealmMemberRole
{
    Owner = 100,
    Moderator = 50,
    Normal = 0
}

public class RealmMember : ModelBase
{
    public long RealmId { get; set; }
    [JsonIgnore] public Realm  Realm { get; set; } = null!;
    public long AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
    
    public RealmMemberRole Role { get; set; }
    public Instant? JoinedAt { get; set; }
}