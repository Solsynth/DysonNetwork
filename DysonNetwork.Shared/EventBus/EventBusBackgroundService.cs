using System.Collections.Concurrent;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
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
    ILogger<EventBusBackgroundService> logger,
    IEnumerable<EventSubscription> subscriptions
)
    : BackgroundService
{
    private readonly List<EventSubscription> _subscriptions = subscriptions.ToList();
    private readonly ConcurrentDictionary<string, int> _retryCounts = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Count == 0)
        {
            logger.LogInformation(
                "No event subscriptions registered, EventBusBackgroundService will not start listeners");
            return;
        }

        logger.LogInformation("Starting EventBusBackgroundService with {Count} subscriptions", _subscriptions.Count);

        var tasks = _subscriptions.Select(sub => ProcessSubscriptionAsync(sub, stoppingToken));
        await Task.WhenAll(tasks);
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
            await js.EnsureStreamCreated(subscription.StreamName, new[] { subscription.Subject });

            var consumer = await js.CreateOrUpdateConsumerAsync(
                subscription.StreamName,
                new ConsumerConfig(subscription.ConsumerName),
                cancellationToken: stoppingToken
            );

            logger.LogInformation(
                "Starting JetStream subscription on stream: {Stream}, consumer: {Consumer}, subject: {Subject}",
                subscription.StreamName, subscription.ConsumerName, subscription.Subject
            );

            await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
            {
                var success = await HandleJetStreamMessageAsync(msg, subscription, stoppingToken);

                if (success)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                }
                else
                {
                    await msg.NakAsync(cancellationToken: stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in JetStream subscription for subject: {Subject}", subscription.Subject);
            throw;
        }
    }

    private async Task HandleMessageAsync(INatsMsg<byte[]> msg, EventSubscription subscription,
        CancellationToken stoppingToken)
    {
        var _ = await ProcessMessageInternalAsync(msg.Data, msg.Headers, subscription, stoppingToken);
    }

    private async Task<bool> HandleJetStreamMessageAsync(INatsJSMsg<byte[]> msg, EventSubscription subscription,
        CancellationToken stoppingToken)
    {
        return await ProcessMessageInternalAsync(msg.Data, msg.Headers, subscription, stoppingToken);
    }

    private async Task<bool> ProcessMessageInternalAsync(byte[] data, NATS.Client.Core.NatsHeaders? headers,
        EventSubscription subscription, CancellationToken stoppingToken)
    {
        var retryKey = $"{subscription.Subject}:{Guid.NewGuid()}";

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var eventPayload =
                JsonSerializer.Deserialize(json, subscription.EventType, InfraObjectCoder.SerializerOptions);

            if (eventPayload == null)
            {
                logger.LogWarning("Failed to deserialize event for subject: {Subject}", subscription.Subject);
                return true; // Ack to prevent redelivery of malformed messages
            }

            await using var scope = serviceProvider.CreateAsyncScope();
            var context = new EventContext
            {
                Subject = subscription.Subject,
                Headers = headers?.ToDictionary(h => h.Key, h => string.Join(",", h.Value)) ?? new(),
                CancellationToken = stoppingToken,
                ServiceProvider = scope.ServiceProvider
            };

            var handler = subscription.Handler;
            var task = (Task?)handler.DynamicInvoke(eventPayload, context);

            if (task != null)
            {
                await task;
            }

            logger.LogDebug("Successfully handled event {EventType} on subject {Subject}",
                subscription.EventType.Name, subscription.Subject);

            _retryCounts.TryRemove(retryKey, out _);
            return true;
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
                return true; // Ack to prevent infinite redelivery
            }

            logger.LogWarning(ex, "Error handling event on subject {Subject} (retry {RetryCount}/{MaxRetries})",
                subscription.Subject, retryCount, subscription.MaxRetries);

            return false;
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