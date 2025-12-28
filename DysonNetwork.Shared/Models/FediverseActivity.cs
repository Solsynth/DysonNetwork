using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnFediverseActivity : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(2048)]
    public string Uri { get; set; } = null!;
    
    public FediverseActivityType Type { get; set; }
    
    [MaxLength(2048)]
    public string? ObjectUri { get; set; }
    
    [MaxLength(2048)]
    public string? TargetUri { get; set; }
    
    public Instant? PublishedAt { get; set; }
    
    public bool IsLocal { get; set; }
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? RawData { get; set; }
    
    public Guid ActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor Actor { get; set; } = null!;
    
    public Guid? ContentId { get; set; }
    [JsonIgnore]
    public SnFediverseContent? Content { get; set; }
    
    public Guid? TargetActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor? TargetActor { get; set; }
    
    public Guid? LocalPostId { get; set; }
    public Guid? LocalAccountId { get; set; }
    
    public ActivityStatus Status { get; set; } = ActivityStatus.Pending;
    
    [MaxLength(4096)]
    public string? ErrorMessage { get; set; }
}

public enum FediverseActivityType
{
    Create,
    Update,
    Delete,
    Follow,
    Unfollow,
    Like,
    Announce,
    Undo,
    Accept,
    Reject,
    Add,
    Remove,
    Block,
    Unblock,
    Flag,
    Move
}

public enum ActivityStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
