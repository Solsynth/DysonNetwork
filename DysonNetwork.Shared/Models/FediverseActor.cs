using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Uri), IsUnique = true)]
public class SnFediverseActor : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(2048)] public string Type { get; set; } = "Person";
    [MaxLength(2048)] public string Uri { get; set; } = null!;
    [MaxLength(256)] public string Username { get; set; } = null!;
    [MaxLength(2048)] public string? DisplayName { get; set; }
    [MaxLength(4096)] public string? Bio { get; set; }
    [MaxLength(2048)] public string? InboxUri { get; set; }
    [MaxLength(2048)] public string? OutboxUri { get; set; }
    [MaxLength(2048)] public string? FollowersUri { get; set; }
    [MaxLength(2048)] public string? FollowingUri { get; set; }
    [MaxLength(2048)] public string? FeaturedUri { get; set; }
    [MaxLength(2048)] public string? PublicKeyId { get; set; }
    [MaxLength(8192)] public string? PublicKey { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Metadata { get; set; }
    [MaxLength(2048)] public string? AvatarUrl { get; set; }
    [MaxLength(2048)] public string? HeaderUrl { get; set; }
    
    public bool IsBot { get; set; } = false;
    public bool IsLocked { get; set; } = false;
    public bool IsDiscoverable { get; set; } = true;
    
    public Guid InstanceId { get; set; }
    public SnFediverseInstance Instance { get; set; } = null!;
    
    [JsonIgnore] public ICollection<SnFediverseContent> Contents { get; set; } = [];
    [JsonIgnore] public ICollection<SnFediverseActivity> Activities { get; set; } = [];
    [JsonIgnore] public ICollection<SnFediverseRelationship> FollowingRelationships { get; set; } = [];
    [JsonIgnore] public ICollection<SnFediverseRelationship> FollowerRelationships { get; set; } = [];
    
    public Instant? LastFetchedAt { get; set; }
    public Instant? LastActivityAt { get; set; }
    
    public Guid? PublisherId { get; set; }
}
