namespace DysonNetwork.Shared.EventBus;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventPayload, CancellationToken cancellationToken = default) where TEvent : IEvent;
    Task PublishAsync<TEvent>(string subject, TEvent eventPayload, CancellationToken cancellationToken = default) where TEvent : IEvent;
}
