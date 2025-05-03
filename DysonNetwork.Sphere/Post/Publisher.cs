using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public enum PublisherType
{
    Individual,
    Organizational
}

[Index(nameof(Name), IsUnique = true)]
public class Publisher : ModelBase
{
    public long Id { get; set; }
    public PublisherType PublisherType { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    public string? PictureId { get; set; }
    public CloudFile? Picture { get; set; }
    public string? BackgroundId { get; set; }
    public CloudFile? Background { get; set; }

    [JsonIgnore] public ICollection<Post> Posts { get; set; } = new List<Post>();
    [JsonIgnore] public ICollection<PostCollection> Collections { get; set; } = new List<PostCollection>();
    [JsonIgnore] public ICollection<PublisherMember> Members { get; set; } = new List<PublisherMember>();
    
    public long? AccountId { get; set; }
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

    public PublisherMemberRole Role { get; set; } = PublisherMemberRole.Viewer;
    public Instant? JoinedAt { get; set; }
}