using System.Text.Json;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Net;

namespace DysonNetwork.Shared.EventBus;

public class EventBus : IEventBus
{
    private readonly INatsConnection _nats;
    private readonly ILogger<EventBus> _logger;

    public EventBus(INatsConnection nats, ILogger<EventBus> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(TEvent eventPayload, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        return PublishAsync(eventPayload.EventType, eventPayload, cancellationToken);
    }

    public async Task PublishAsync<TEvent>(string subject, TEvent eventPayload, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        try
        {
            var json = JsonSerializer.Serialize(eventPayload, GrpcTypeHelper.SerializerOptions);
            var data = System.Text.Encoding.UTF8.GetBytes(json);
            
            await _nats.PublishAsync(subject, data, cancellationToken: cancellationToken);
            
            _logger.LogDebug("Published event {EventType} to subject {Subject}", eventPayload.EventType, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to subject {Subject}", eventPayload.EventType, subject);
            throw;
        }
    }
}
