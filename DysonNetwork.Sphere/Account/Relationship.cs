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
    public long AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public long RelatedId { get; set; }
    public Account Related { get; set; } = null!;

    public Instant? ExpiredAt { get; set; }

    public RelationshipStatus Status { get; set; } = RelationshipStatus.Pending;
}