using NodaTime;

namespace DysonNetwork.Pass.Account;

public enum RelationshipStatus : short
{
    Friends = 100,
    Pending = 0,
    Blocked = -100
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