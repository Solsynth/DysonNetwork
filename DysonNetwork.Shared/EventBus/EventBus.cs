using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Shared.EventBus;

public class EventBus(INatsConnection nats, ILogger<EventBus> logger) : IEventBus
{
    private readonly HashSet<string> _ensuredSubjects = new();
    private readonly SemaphoreSlim _streamLock = new(1, 1);

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
            
            logger.LogDebug("Publishing event {EventType} to subject {Subject}. JSON length: {Length}", 
                eventPayload.EventType, subject, json.Length);
            
            var js = nats.CreateJetStreamContext();
            
            // Ensure stream exists before publishing
            await EnsureStreamExistsAsync(js, subject, cancellationToken);
            
            logger.LogDebug("Stream ensured, publishing to {Subject}...", subject);
            var ack = await js.PublishAsync(subject, data, cancellationToken: cancellationToken);
            logger.LogInformation("Published event {EventType} to subject {Subject} via JetStream. Stream: {Stream}", 
                eventPayload.EventType, subject, ack?.Stream ?? "n/a");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish event {EventType} to subject {Subject}", eventPayload.EventType, subject);
            throw;
        }
    }

    private async Task EnsureStreamExistsAsync(INatsJSContext js, string subject, CancellationToken cancellationToken)
    {
        // Check if we've already ensured this subject
        if (_ensuredSubjects.Contains(subject))
        {
            logger.LogDebug("Subject {Subject} already ensured", subject);
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
            var streamName = GetStreamName(subject);
            logger.LogDebug("Ensuring stream {StreamName} exists for subject {Subject}", streamName, subject);

            try
            {
                // Try to get the stream first
                var stream = await js.GetStreamAsync(streamName, cancellationToken: cancellationToken);
                logger.LogDebug("Stream {StreamName} already exists", streamName);
                
                // Check if this subject is already covered by the stream
                var currentSubjects = stream.Info.Config.Subjects?.ToList() ?? new List<string>();
                if (!currentSubjects.Contains(subject))
                {
                    // Update stream to include this subject
                    logger.LogInformation("Updating stream {StreamName} to include subject {Subject}", streamName, subject);
                    currentSubjects.Add(subject);
                    await js.UpdateStreamAsync(new StreamConfig(streamName, currentSubjects), cancellationToken: cancellationToken);
                    logger.LogInformation("Updated stream {StreamName} to include subject {Subject}", streamName, subject);
                }
                else
                {
                    logger.LogDebug("Stream {StreamName} already includes subject {Subject}", streamName, subject);
                }
                
                _ensuredSubjects.Add(subject);
            }
            catch (NatsJSException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("No stream matches"))
            {
                // Stream doesn't exist, create it
                logger.LogInformation("Creating stream {StreamName} for subject {Subject}", streamName, subject);
                try
                {
                    await js.CreateStreamAsync(new StreamConfig(streamName, [subject]), cancellationToken: cancellationToken);
                    _ensuredSubjects.Add(subject);
                    logger.LogInformation("Created JetStream stream {StreamName} for subject {Subject}", streamName, subject);
                }
                catch (NatsJSApiException apiEx) when (apiEx.Message.Contains("already exists") || apiEx.Message.Contains("overlap"))
                {
                    logger.LogWarning("Stream creation conflict for {StreamName}: {Message}. Fetching existing stream.", streamName, apiEx.Message);
                    // Stream was created by another instance, fetch it
                    try
                    {
                        var existingStream = await js.GetStreamAsync(streamName, cancellationToken: cancellationToken);
                        _ensuredSubjects.Add(subject);
                        logger.LogDebug("Found existing stream {StreamName}", streamName);
                    }
                    catch (Exception getEx)
                    {
                        logger.LogError(getEx, "Could not fetch existing stream {StreamName}", streamName);
                        throw;
                    }
                }
            }
            catch (NatsJSApiException apiEx) when (apiEx.Message.Contains("overlap"))
            {
                logger.LogWarning("Subject {Subject} overlaps with existing stream. This may be expected if another service created it.", subject);
                _ensuredSubjects.Add(subject);
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
