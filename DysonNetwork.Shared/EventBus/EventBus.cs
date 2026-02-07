using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Shared.EventBus;

public class EventBus : IEventBus
{
    private readonly INatsConnection _nats;
    private readonly ILogger<EventBus> _logger;
    private readonly HashSet<string> _ensuredStreams = new();

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
            var json = JsonSerializer.Serialize(eventPayload, InfraObjectCoder.SerializerOptions);
            var data = System.Text.Encoding.UTF8.GetBytes(json);
            
            var js = _nats.CreateJetStreamContext();
            
            // Ensure stream exists before publishing
            await EnsureStreamExistsAsync(js, subject, cancellationToken);
            
            await js.PublishAsync(subject, data, cancellationToken: cancellationToken);
            _logger.LogDebug("Published event {EventType} to subject {Subject} via JetStream", eventPayload.EventType, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to subject {Subject}", eventPayload.EventType, subject);
            throw;
        }
    }

    private async Task EnsureStreamExistsAsync(INatsJSContext js, string subject, CancellationToken cancellationToken)
    {
        // Use subject as stream name (replace dots with underscores for valid stream name)
        var streamName = subject.Replace('.', '_');
        
        if (_ensuredStreams.Contains(streamName))
        {
            return;
        }

        try
        {
            // Try to get the stream first
            await js.GetStreamAsync(streamName, cancellationToken: cancellationToken);
            _ensuredStreams.Add(streamName);
        }
        catch (NatsJSException)
        {
            // Stream doesn't exist, create it
            try
            {
                await js.CreateStreamAsync(new StreamConfig(streamName, [subject]), cancellationToken: cancellationToken);
                _ensuredStreams.Add(streamName);
                _logger.LogInformation("Created JetStream stream {StreamName} for subject {Subject}", streamName, subject);
            }
            catch (NatsJSException ex) when (ex.Message.Contains("already exists"))
            {
                // Stream was created by another instance, that's fine
                _ensuredStreams.Add(streamName);
            }
        }
    }
}
