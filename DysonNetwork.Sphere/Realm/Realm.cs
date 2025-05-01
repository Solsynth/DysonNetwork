using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Realm;

public class Realm : ModelBase
{
    public long Id { get; set; }
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;

    public CloudFile? Picture { get; set; }
    public CloudFile? Background { get; set; }
    
    [JsonIgnore] public ICollection<RealmMember> Members { get; set; } = new List<RealmMember>();

    public long AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; }
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