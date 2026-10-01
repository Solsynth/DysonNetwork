using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Models;

public class SnFediverseRelationship : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
    public Guid TargetPublisherId { get; set; }
    public SnPublisher TargetPublisher { get; set; } = null!;

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
