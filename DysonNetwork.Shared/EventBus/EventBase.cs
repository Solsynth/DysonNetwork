using NodaTime;

namespace DysonNetwork.Shared.EventBus;

public interface IEvent
{
    Guid EventId { get; }
    Instant Timestamp { get; }
    string EventType { get; }
}

public abstract class EventBase : IEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Instant Timestamp { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public abstract string EventType { get; }
}
