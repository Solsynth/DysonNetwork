using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum RelationshipStatus : short
{
    Friends = 100,
    Pending = 0,
    Blocked = -100
}

public class SnAccountRelationship : ModelBase
{
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
    public Guid RelatedId { get; set; }
    public SnAccount Related { get; set; } = null!;

    public Instant? ExpiredAt { get; set; }

    public RelationshipStatus Status { get; set; } = RelationshipStatus.Pending;
    
    public Proto.Relationship ToProtoValue() => new()
    {
        AccountId = AccountId.ToString(),
        RelatedId = RelatedId.ToString(),
        Account = Account?.ToProtoValue(),
        Related = Related?.ToProtoValue(),
        Status = (int)Status,
        CreatedAt = CreatedAt.ToDateTimeUtc() != default ? CreatedAt.ToTimestamp() : null,
        UpdatedAt = UpdatedAt.ToDateTimeUtc() != default ? UpdatedAt.ToTimestamp() : null
    };
}