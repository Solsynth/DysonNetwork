using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public enum PublisherType
{
    Individual,
    Organizational
}

public class Publisher : ModelBase
{
    public long Id { get; set; }
    public PublisherType PublisherType { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    public CloudFile? Picture { get; set; }
    public CloudFile? Background { get; set; }

    [JsonIgnore] public ICollection<Post> Posts { get; set; } = new List<Post>();
    [JsonIgnore] public ICollection<PublisherMember> Members { get; set; } = new List<PublisherMember>();
    [JsonIgnore] public Account.Account? Account { get; set; }
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
    public long PublisherId { get; set; }
    [JsonIgnore] public Publisher Publisher { get; set; } = null!;
    public long AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;

    public PublisherMemberRole Role { get; set; }
    public Instant? JoinedAt { get; set; }
}