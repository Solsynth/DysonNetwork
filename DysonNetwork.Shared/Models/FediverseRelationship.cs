using System.ComponentModel.DataAnnotations;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnFediverseRelationship : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ActorId { get; set; }
    public SnFediverseActor Actor { get; set; } = null!;
    public Guid TargetActorId { get; set; }
    public SnFediverseActor TargetActor { get; set; } = null!;

    public RelationshipState State { get; set; } = RelationshipState.Pending;

    public bool IsMuting { get; set; } = false;
    public bool IsBlocking { get; set; } = false;

    public Instant? FollowedAt { get; set; }

    [MaxLength(4096)] public string? RejectReason { get; set; }

    public Guid? RealmId { get; set; }
}

public enum RelationshipState
{
    Pending,
    Accepted,
    Rejected
}
