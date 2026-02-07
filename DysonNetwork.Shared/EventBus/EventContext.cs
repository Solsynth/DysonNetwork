namespace DysonNetwork.Shared.EventBus;

public class EventContext
{
    public string Subject { get; set; } = null!;
    public Dictionary<string, string> Headers { get; set; } = new();
    public CancellationToken CancellationToken { get; set; }
    public IServiceProvider ServiceProvider { get; set; } = null!;
}

public delegate Task EventHandler<in TEvent>(TEvent eventPayload, EventContext context) where TEvent : IEvent;

public class EventSubscription
{
    public required string Subject { get; init; }
    public required Type EventType { get; init; }
    public required Delegate Handler { get; init; }
    public int MaxRetries { get; init; } = 3;
    public int Parallelism { get; init; } = 1;
    public bool UseJetStream { get; init; } = false;
    public string? ConsumerName { get; init; }
    public string? StreamName { get; init; }
}
