using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class AccountDeletedEvent : EventBase
{
    public static string Type => "account_deleted";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; } = Guid.NewGuid();
    public Instant DeletedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountStatusUpdatedEvent : EventBase
{
    public static string Type => "account_status_updated";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public SnAccountStatus Status { get; set; } = new();
    public Instant UpdatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountCreatedEvent : EventBase
{
    public static string Type => "accounts.created";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public string Region { get; set; } = "en";
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; }
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class AccountIdentityUpsertedEvent : EventBase
{
    public static string Type => "account_identity_upserted";
    public override string EventType => Type;
    public override string StreamName => "account_events";

    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public string Region { get; set; } = "en";
    public Instant? ActivatedAt { get; set; }
    public bool IsSuperuser { get; set; }
    public Instant UpdatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
