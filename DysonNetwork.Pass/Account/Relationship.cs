using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

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
    
    public Shared.Proto.Relationship ToProtoValue() => new()
    {
        AccountId = AccountId.ToString(),
        RelatedId = RelatedId.ToString(),
        Account = Account.ToProtoValue(),
        Related = Related.ToProtoValue(),
        Status = (int)Status,
        CreatedAt = CreatedAt.ToTimestamp(),
        UpdatedAt = UpdatedAt.ToTimestamp()
    };
}