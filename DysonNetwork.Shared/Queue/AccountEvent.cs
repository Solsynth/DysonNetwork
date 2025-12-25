using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class AccountDeletedEvent
{
    public static string Type => "account_deleted";

    public Guid AccountId { get; set; } = Guid.NewGuid();
    public Instant DeletedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountStatusUpdatedEvent
{
    public static string Type => "account_status_updated";

    public Guid AccountId { get; set; }
    public SnAccountStatus Status { get; set; } = new();
    public Instant UpdatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
