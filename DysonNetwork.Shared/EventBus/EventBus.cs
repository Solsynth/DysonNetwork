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
    private readonly HashSet<string> _ensuredSubjects = new();
    private readonly SemaphoreSlim _streamLock = new(1, 1);

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
        // Check if we've already ensured this subject
        if (_ensuredSubjects.Contains(subject))
        {
            return;
        }

        await _streamLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_ensuredSubjects.Contains(subject))
            {
                return;
            }

            // Use a consistent stream name based on the subject
            // Replace dots with underscores and take the first part for grouping
            var streamName = GetStreamName(subject);

            try
            {
                // Try to get the stream first
                var stream = await js.GetStreamAsync(streamName, cancellationToken: cancellationToken);
                
                // Check if this subject is already covered by the stream
                if (stream.Info.Config.Subjects?.Contains(subject) != true)
                {
                    // Update stream to include this subject
                    var subjects = stream.Info.Config.Subjects?.ToList() ?? new List<string>();
                    subjects.Add(subject);
                    await js.UpdateStreamAsync(new StreamConfig(streamName, subjects), cancellationToken: cancellationToken);
                    _logger.LogInformation("Updated stream {StreamName} to include subject {Subject}", streamName, subject);
                }
                
                _ensuredSubjects.Add(subject);
            }
            catch (NatsJSException)
            {
                // Stream doesn't exist, create it
                try
                {
                    await js.CreateStreamAsync(new StreamConfig(streamName, [subject]), cancellationToken: cancellationToken);
                    _ensuredSubjects.Add(subject);
                    _logger.LogInformation("Created JetStream stream {StreamName} for subject {Subject}", streamName, subject);
                }
                catch (NatsJSApiException apiEx) when (apiEx.Message.Contains("already exists") || apiEx.Message.Contains("overlap"))
                {
                    // Stream was created by another instance or subject overlaps, that's fine
                    // Try to get the existing stream
                    try
                    {
                        var existingStream = await js.GetStreamAsync(streamName, cancellationToken: cancellationToken);
                        _ensuredSubjects.Add(subject);
                        _logger.LogDebug("Stream {StreamName} already exists (created by another instance)", streamName);
                    }
                    catch
                    {
                        // If we can't get it, assume it exists and mark as ensured anyway
                        _ensuredSubjects.Add(subject);
                    }
                }
            }
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private static string GetStreamName(string subject)
    {
        // Group related subjects into the same stream
        // e.g., "payment_orders" -> "payment_orders"
        // e.g., "account_deleted" -> "account_events" 
        // e.g., "websocket_connected" -> "websocket_events"
        
        var parts = subject.Split('.');
        
        // Special grouping rules
        if (subject.StartsWith("payment"))
            return "payment_events";
        if (subject.StartsWith("account"))
            return "account_events";
        if (subject.StartsWith("websocket"))
            return "websocket_events";
        if (subject.StartsWith("file"))
            return "file_events";
        
        // Default: use subject as stream name (replace dots with underscores)
        return subject.Replace('.', '_');
    }
}
