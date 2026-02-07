using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class AccountDeletedEvent : EventBase
{
    public static string Type => "account_deleted";
    public override string EventType => Type;

    public Guid AccountId { get; set; } = Guid.NewGuid();
    public Instant DeletedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountStatusUpdatedEvent : EventBase
{
    public static string Type => "account_status_updated";
    public override string EventType => Type;

    public Guid AccountId { get; set; }
    public SnAccountStatus Status { get; set; } = new();
    public Instant UpdatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
