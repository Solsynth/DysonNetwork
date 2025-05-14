using NodaTime;

namespace DysonNetwork.Sphere.Account;

public enum RelationshipStatus
{
    Pending,
    Friends,
    Blocked
}

public class Relationship : ModelBase
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid RelatedId { get; set; }
    public Account Related { get; set; } = null!;

    public Instant? ExpiredAt { get; set; }

    public RelationshipStatus Status { get; set; } = RelationshipStatus.Pending;
}