using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.EventBus;

public interface IEventBusBuilder
{
    IServiceCollection Services { get; }
    string ServiceName { get; }
    
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
    public bool AutoAck { get; set; } = true; // Whether to auto-ack after successful handler execution
}

public class EventBusBuilder : IEventBusBuilder
{
    private readonly List<EventSubscription> _subscriptions = new();

    public EventBusBuilder(IServiceCollection services, string serviceName)
    {
        Services = services;
        ServiceName = serviceName;
        services.AddSingleton(_subscriptions);
    }

    public IServiceCollection Services { get; }
    public string ServiceName { get; }

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

        // If consumer name not specified, generate a unique one per service
        if (string.IsNullOrEmpty(options.ConsumerName))
        {
            var eventTypeName = typeof(TEvent).Name;
            var serviceIdentifier = ServiceName.ToLowerInvariant().Replace(".", "_");
            options.ConsumerName = $"{serviceIdentifier}_{eventTypeName.ToLowerInvariant()}_consumer";
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
            StreamName = options.StreamName,
            AutoAck = options.AutoAck
        };

        _subscriptions.Add(subscription);
        
        return this;
    }
}
