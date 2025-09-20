using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Data;

public class SubscriptionReference
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public Instant BegunAt { get; set; }
    public Instant? EndedAt { get; set; }
    public Instant? RenewalAt { get; set; }
    public SubscriptionReferenceStatus Status { get; set; }
    public Guid AccountId { get; set; }

    public static SubscriptionReference FromProtoValue(Proto.SubscriptionReferenceObject proto)
    {
        return new SubscriptionReference
        {
            Id = Guid.Parse(proto.Id),
            Identifier = proto.Identifier,
            DisplayName = proto.DisplayName,
            IsActive = proto.IsActive,
            IsAvailable = proto.IsAvailable,
            BegunAt = proto.BegunAt.ToInstant(),
            EndedAt = proto.EndedAt?.ToInstant(),
            RenewalAt = proto.RenewalAt?.ToInstant(),
            Status = (SubscriptionReferenceStatus)proto.Status,
            AccountId = Guid.Parse(proto.AccountId),
        };
    }

    public Proto.SubscriptionReferenceObject ToProtoValue()
    {
        return new Proto.SubscriptionReferenceObject
        {
            Id = Id.ToString(),
            Identifier = Identifier,
            DisplayName = DisplayName,
            IsActive = IsActive,
            IsAvailable = IsAvailable,
            BegunAt = BegunAt.ToTimestamp(),
            EndedAt = EndedAt?.ToTimestamp(),
            RenewalAt = RenewalAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            Status = Status switch
            {
                SubscriptionReferenceStatus.Unpaid => Proto.SubscriptionStatus.Unpaid,
                SubscriptionReferenceStatus.Active => Proto.SubscriptionStatus.Active,
                SubscriptionReferenceStatus.Expired => Proto.SubscriptionStatus.Expired,
                SubscriptionReferenceStatus.Cancelled => Proto.SubscriptionStatus.Cancelled,
                _ => Proto.SubscriptionStatus.Unpaid
            }
        };
    }
}

public enum SubscriptionReferenceStatus
{
    Unpaid = 0,
    Active = 1,
    Expired = 2,
    Cancelled = 3
}