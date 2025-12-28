using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnFediverseRelationship : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor Actor { get; set; } = null!;
    
    public Guid TargetActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor TargetActor { get; set; } = null!;
    
    public RelationshipState State { get; set; } = RelationshipState.Pending;
    
    public bool IsFollowing { get; set; } = false;
    public bool IsFollowedBy { get; set; } = false;
    
    public bool IsMuting { get; set; } = false;
    public bool IsBlocking { get; set; } = false;
    
    public Instant? FollowedAt { get; set; }
    public Instant? FollowedBackAt { get; set; }
    
    [MaxLength(4096)]
    public string? RejectReason { get; set; }
    
    public bool IsLocalActor { get; set; }
    
    public Guid? LocalAccountId { get; set; }
    public Guid? LocalPublisherId { get; set; }
}

public enum RelationshipState
{
    Pending,
    Accepted,
    Rejected
}
