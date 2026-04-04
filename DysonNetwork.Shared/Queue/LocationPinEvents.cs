using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class LocationPinCreatedEvent : EventBase
{
    public static string Type => "locationpin.created";
    public override string EventType => Type;
    public override string StreamName => "locationpin_events";

    public Guid PinId { get; set; }
    public Guid AccountId { get; set; }
    public LocationVisibility Visibility { get; set; }
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class LocationPinUpdatedEvent : EventBase
{
    public static string Type => "locationpin.updated";
    public override string EventType => Type;
    public override string StreamName => "locationpin_events";

    public const string SubjectPrefix = "locationpin.updated.";

    public Guid PinId { get; set; }
    public Guid AccountId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public string? LocationAddress { get; set; }
    public Instant UpdatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class LocationPinRemovedEvent : EventBase
{
    public static string Type => "locationpin.removed";
    public override string EventType => Type;
    public override string StreamName => "locationpin_events";

    public Guid PinId { get; set; }
    public Guid AccountId { get; set; }
    public Instant RemovedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}

public class LocationPinOfflineEvent : EventBase
{
    public static string Type => "locationpin.offline";
    public override string EventType => Type;
    public override string StreamName => "locationpin_events";

    public Guid PinId { get; set; }
    public Guid AccountId { get; set; }
    public Instant OfflineAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
