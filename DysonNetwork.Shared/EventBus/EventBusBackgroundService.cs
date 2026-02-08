using System.Collections.Concurrent;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Shared.EventBus;

public class EventBusBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<EventBusBackgroundService> logger
)
    : BackgroundService
{
    private readonly ConcurrentDictionary<string, int> _retryCounts = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get subscriptions from DI container at execution time (not construction time)
        // This ensures all subscriptions added via fluent API are available
        await using var scope = serviceProvider.CreateAsyncScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<List<EventSubscription>>();
        
        // Always start the listeners, even if no subscriptions
        // This allows dynamic subscription scenarios
        if (subscriptions.Count == 0)
        {
            logger.LogInformation(
                "No event subscriptions registered yet. EventBusBackgroundService is running and will listen when subscriptions are added.");
        }
        else
        {
            logger.LogInformation("Starting EventBusBackgroundService with {Count} subscriptions", subscriptions.Count);
            
            foreach (var sub in subscriptions)
            {
                logger.LogInformation("Registered subscription: Subject={Subject}, EventType={EventType}, UseJetStream={UseJetStream}, Stream={StreamName}, Consumer={ConsumerName}",
                    sub.Subject, sub.EventType.Name, sub.UseJetStream, sub.StreamName, sub.ConsumerName);
            }

            var tasks = subscriptions.Select(sub => ProcessSubscriptionAsync(sub, stoppingToken));
            await Task.WhenAll(tasks);
        }
    }

    private async Task ProcessSubscriptionAsync(EventSubscription subscription, CancellationToken stoppingToken)
    {
        if (subscription.UseJetStream)
        {
            await ProcessJetStreamSubscriptionAsync(subscription, stoppingToken);
        }
        else
        {
            await ProcessCoreSubscriptionAsync(subscription, stoppingToken);
        }
    }

    private async Task ProcessCoreSubscriptionAsync(EventSubscription subscription, CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var nats = scope.ServiceProvider.GetRequiredService<INatsConnection>();

        logger.LogInformation("Starting NATS Core subscription on subject: {Subject}", subscription.Subject);

        await foreach (var msg in nats.SubscribeAsync<byte[]>(subscription.Subject, cancellationToken: stoppingToken))
        {
            logger.LogInformation("Received message on subject: {Subject}", subscription.Subject);
            await HandleMessageAsync(msg, subscription, stoppingToken);
        }
    }

    private async Task ProcessJetStreamSubscriptionAsync(EventSubscription subscription,
        CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var nats = scope.ServiceProvider.GetRequiredService<INatsConnection>();
        var js = nats.CreateJetStreamContext();

        if (string.IsNullOrEmpty(subscription.StreamName) || string.IsNullOrEmpty(subscription.ConsumerName))
        {
            logger.LogError("JetStream subscription missing StreamName or ConsumerName for subject: {Subject}",
                subscription.Subject);
            return;
        }

        try
        {
            logger.LogInformation("Ensuring stream {StreamName} exists for subject {Subject}", 
                subscription.StreamName, subscription.Subject);
            
            await js.EnsureStreamCreated(subscription.StreamName, new[] { subscription.Subject });

            logger.LogInformation("Creating/updating consumer {ConsumerName} on stream {StreamName}",
                subscription.ConsumerName, subscription.StreamName);

            var consumer = await js.CreateOrUpdateConsumerAsync(
                subscription.StreamName,
                new ConsumerConfig(subscription.ConsumerName)
                {
                    FilterSubject = subscription.Subject
                },
                cancellationToken: stoppingToken
            );

            logger.LogInformation(
                "Starting JetStream subscription on stream: {Stream}, consumer: {Consumer}, subject: {Subject}",
                subscription.StreamName, subscription.ConsumerName, subscription.Subject
            );

            var messageCount = 0;
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
            {
                messageCount++;
                logger.LogInformation("Received JetStream message #{Count} on subject: {Subject}, stream: {Stream}, consumer: {Consumer}", 
                    messageCount, subscription.Subject, subscription.StreamName, subscription.ConsumerName);
                
                var (success, shouldAck) = await HandleJetStreamMessageAsync(msg, subscription, stoppingToken);

                if (success && shouldAck)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    logger.LogInformation("Message acknowledged successfully");
                }
                else if (!shouldAck)
                {
                    await msg.NakAsync(cancellationToken: stoppingToken);
                    logger.LogInformation("Message negatively acknowledged (handler requested no ack)");
                }
                else
                {
                    await msg.NakAsync(cancellationToken: stoppingToken);
                    logger.LogWarning("Message negatively acknowledged for retry due to error");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in JetStream subscription for subject: {Subject}", subscription.Subject);
            throw;
        }
    }

    private async Task<(bool Success, bool ShouldAck)> HandleMessageAsync(INatsMsg<byte[]> msg, EventSubscription subscription,
        CancellationToken stoppingToken)
    {
        return await ProcessMessageInternalAsync(msg.Data, msg.Headers, subscription, stoppingToken);
    }

    private async Task<(bool Success, bool ShouldAck)> HandleJetStreamMessageAsync(INatsJSMsg<byte[]> msg, EventSubscription subscription,
        CancellationToken stoppingToken)
    {
        return await ProcessMessageInternalAsync(msg.Data, msg.Headers, subscription, stoppingToken);
    }

    private async Task<(bool Success, bool ShouldAck)> ProcessMessageInternalAsync(byte[] data, NatsHeaders? headers,
        EventSubscription subscription, CancellationToken stoppingToken)
    {
        var retryKey = $"{subscription.Subject}:{Guid.NewGuid()}";

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            logger.LogDebug("Processing message: {Json}", json[..Math.Min(json.Length, 200)]);
            
            var eventPayload =
                JsonSerializer.Deserialize(json, subscription.EventType, InfraObjectCoder.SerializerOptions);

            if (eventPayload == null)
            {
                logger.LogWarning("Failed to deserialize event for subject: {Subject}. JSON: {Json}", 
                    subscription.Subject, json[..Math.Min(json.Length, 500)]);
                return (true, true); // Ack to prevent redelivery of malformed messages
            }

            logger.LogDebug("Deserialized event of type {EventType}", eventPayload.GetType().Name);

            await using var scope = serviceProvider.CreateAsyncScope();
            var context = new EventContext
            {
                Subject = subscription.Subject,
                Headers = headers?.ToDictionary(h => h.Key, h => string.Join(",", h.Value)) ?? new(),
                CancellationToken = stoppingToken,
                ServiceProvider = scope.ServiceProvider
            };

            var handler = subscription.Handler;
            logger.LogDebug("Invoking handler for {EventType} on subject {Subject}", 
                subscription.EventType.Name, subscription.Subject);
            
            var task = (Task?)handler.DynamicInvoke(eventPayload, context);

            if (task != null)
            {
                await task;
            }

            // Check if handler wants to skip acknowledgment
            var shouldAck = subscription.AutoAck && context.ShouldAcknowledge;
            
            if (!shouldAck)
            {
                logger.LogInformation("Handler for {EventType} on subject {Subject} requested no acknowledgment",
                    subscription.EventType.Name, subscription.Subject);
            }
            else
            {
                logger.LogInformation("Successfully handled event {EventType} on subject {Subject}",
                    subscription.EventType.Name, subscription.Subject);
            }

            _retryCounts.TryRemove(retryKey, out _);
            return (true, shouldAck);
        }
        catch (Exception ex)
        {
            var retryCount = _retryCounts.AddOrUpdate(retryKey, 1, (_, count) => count + 1);

            if (retryCount >= subscription.MaxRetries)
            {
                logger.LogError(ex,
                    "Max retries ({MaxRetries}) exceeded for event on subject {Subject}. Dropping message.",
                    subscription.MaxRetries, subscription.Subject);
                _retryCounts.TryRemove(retryKey, out _);
                return (true, true); // Ack to prevent infinite redelivery
            }

            logger.LogWarning(ex, "Error handling event on subject {Subject} (retry {RetryCount}/{MaxRetries})",
                subscription.Subject, retryCount, subscription.MaxRetries);

            return (false, false);
        }
    }
}

public static class JetStreamExtensions
{
    public static async Task<INatsJSStream> EnsureStreamCreated(
        this INatsJSContext context,
        string stream,
        ICollection<string>? subjects
    )
    {
        try
        {
            return await context.CreateStreamAsync(new StreamConfig(stream, subjects ?? []));
        }
        catch (NatsJSException)
        {
            return await context.GetStreamAsync(stream);
        }
    }
}
