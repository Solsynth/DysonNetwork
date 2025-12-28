using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Domain), IsUnique = true)]
public class SnFediverseInstance : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(256)] public string Domain { get; set; } = null!;
    [MaxLength(512)] public string? Name { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    [MaxLength(2048)] public string? Software { get; set; }
    [MaxLength(2048)] public string? Version { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Metadata { get; set; }
    
    public bool IsBlocked { get; set; } = false;
    public bool IsSilenced { get; set; } = false;
    
    [MaxLength(2048)] public string? BlockReason { get; set; }
    [JsonIgnore] public ICollection<SnFediverseActor> Actors { get; set; } = [];
    [JsonIgnore] public ICollection<SnFediverseContent> Contents { get; set; } = [];
    
    public Instant? LastFetchedAt { get; set; }
    public Instant? LastActivityAt { get; set; }
}
