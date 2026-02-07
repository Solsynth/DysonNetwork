using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.EventBus;

public interface IEventBusBuilder
{
    IServiceCollection Services { get; }
    
    IEventBusBuilder AddListener<TEvent>(
        EventHandler<TEvent> handler,
        Action<EventListenerOptions>? configureOptions = null
    ) where TEvent : class, IEvent;
    
    IEventBusBuilder AddListener<TEvent>(
        string subject,
        EventHandler<TEvent> handler,
        Action<EventListenerOptions>? configureOptions = null
    ) where TEvent : class, IEvent;
}

public class EventListenerOptions
{
    public int MaxRetries { get; set; } = 3;
    public int Parallelism { get; set; } = 1;
    public bool UseJetStream { get; set; } = true; // Default to JetStream
    public string? ConsumerName { get; set; }
    public string? StreamName { get; set; }
}

public class EventBusBuilder : IEventBusBuilder
{
    private readonly List<EventSubscription> _subscriptions = new();

    public EventBusBuilder(IServiceCollection services)
    {
        Services = services;
        services.AddSingleton(_subscriptions);
    }

    public IServiceCollection Services { get; }

    public IEventBusBuilder AddListener<TEvent>(
        EventHandler<TEvent> handler,
        Action<EventListenerOptions>? configureOptions = null
    ) where TEvent : class, IEvent
    {
        var options = new EventListenerOptions();
        configureOptions?.Invoke(options);

        var instance = Activator.CreateInstance<TEvent>();
        var subject = instance.EventType;

        return AddListener(subject, handler, configureOptions);
    }

    public IEventBusBuilder AddListener<TEvent>(
        string subject,
        EventHandler<TEvent> handler,
        Action<EventListenerOptions>? configureOptions = null
    ) where TEvent : class, IEvent
    {
        var options = new EventListenerOptions();
        configureOptions?.Invoke(options);

        // If stream name not specified, get it from the event class
        if (string.IsNullOrEmpty(options.StreamName))
        {
            var instance = Activator.CreateInstance<TEvent>();
            options.StreamName = instance.StreamName;
        }

        // If consumer name not specified, generate one from the event type and handler
        if (string.IsNullOrEmpty(options.ConsumerName))
        {
            var eventTypeName = typeof(TEvent).Name;
            var serviceName = Services.FirstOrDefault(s => s.ImplementationType?.Namespace?.Contains("Startup") == true)
                ?.ImplementationType?.Assembly.GetName().Name ?? "unknown";
            options.ConsumerName = $"{serviceName.ToLowerInvariant().Replace(".", "_")}_{eventTypeName.ToLowerInvariant()}_consumer";
        }

        var subscription = new EventSubscription
        {
            Subject = subject,
            EventType = typeof(TEvent),
            Handler = handler,
            MaxRetries = options.MaxRetries,
            Parallelism = options.Parallelism,
            UseJetStream = options.UseJetStream,
            ConsumerName = options.ConsumerName,
            StreamName = options.StreamName
        };

        _subscriptions.Add(subscription);
        
        return this;
    }
}
