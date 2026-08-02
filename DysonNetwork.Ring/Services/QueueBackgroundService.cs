using System.Text.Json;
using DysonNetwork.Ring.Email;
using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Ring.Services;

public class QueueBackgroundService(
    INatsConnection nats,
    IServiceProvider serviceProvider,
    ILogger<QueueBackgroundService> logger,
    IConfiguration configuration
)
    : BackgroundService
{
    public const string QueueName = "pusher_queue";
    public const string StreamName = "pusher_queue";
    private const string QueueGroup = "pusher_workers";
    private const string ConsumerName = "pusher_workers";
    private readonly int _consumerCount = configuration.GetValue<int?>("ConsumerCount") ?? Environment.ProcessorCount;
    private readonly List<Task> _consumerTasks = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting {ConsumerCount} queue consumers", _consumerCount);

        // Start multiple consumers
        for (var i = 0; i < _consumerCount; i++)
            _consumerTasks.Add(Task.Run(() => RunConsumerAsync(stoppingToken), stoppingToken));

        // Wait for all consumers to complete
        await Task.WhenAll(_consumerTasks);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();
        await js.EnsureStreamCreated(StreamName, [QueueName]);

        // Durable push consumer with a deliver group: multiple workers (and
        // instances) share the load, and messages published while Ring is down
        // or restarting survive and are redelivered on boot (DeliverPolicy.All
        // on first creation, ack-tracked afterwards).
        var consumer = await js.CreateOrUpdateConsumerAsync(
            StreamName,
            new ConsumerConfig(ConsumerName)
            {
                FilterSubject = QueueName,
                DeliverGroup = QueueGroup,
                DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                MaxDeliver = 5,
            },
            cancellationToken: stoppingToken
        );

        logger.LogInformation("Queue consumer started");

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            try
            {
                var message = InfraObjectCoder.ConvertByteStringToObject<QueueMessage>(
                    ByteString.CopyFrom(msg.Data ?? [])
                );
                if (message is not null)
                {
                    await ProcessMessageAsync(message, stoppingToken);
                }
                else
                {
                    logger.LogWarning($"Invalid message format for {msg.Subject}");
                }
                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown; leave unacked so the message is redelivered on boot.
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in queue consumer");
                try
                {
                    await msg.NakAsync(cancellationToken: stoppingToken);
                }
                catch (Exception nakEx)
                {
                    logger.LogError(nakEx, "Failed to nack queue message");
                }
            }
        }
    }

    private async ValueTask ProcessMessageAsync(QueueMessage message,
        CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();

        logger.LogDebug("Processing message of type {MessageType}", message.Type);

        switch (message.Type)
        {
            case QueueMessageType.Email:
                await ProcessEmailMessageAsync(message, scope);
                break;

            case QueueMessageType.PushNotification:
                await ProcessPushNotificationMessageAsync(message, scope, cancellationToken);
                break;

            default:
                logger.LogWarning("Unknown message type: {MessageType}", message.Type);
                break;
        }
    }

    private static async Task ProcessEmailMessageAsync(QueueMessage message, IServiceScope scope)
    {
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
        var emailMessage = JsonSerializer.Deserialize<EmailMessage>(message.Data)
                           ?? throw new InvalidOperationException("Invalid email message format");

        await emailService.SendEmailAsync(
            emailMessage.ToName,
            emailMessage.ToAddress,
            emailMessage.Subject,
            emailMessage.Body,
            "queue");
    }

    private static async Task ProcessPushNotificationMessageAsync(QueueMessage message, IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var pushService = scope.ServiceProvider.GetRequiredService<PushService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<QueueBackgroundService>>();

        var notification = JsonSerializer.Deserialize<Shared.Models.SnNotification>(message.Data);
        if (notification == null)
        {
            logger.LogError("Invalid push notification data format");
            return;
        }

        logger.LogDebug("Processing push notification for account {AccountId}", notification.AccountId);
        await pushService.DeliverPushNotification(
            notification,
            message.ExcludedWebSocketDeviceIds,
            message.IsSavable,
            cancellationToken
        );
        logger.LogDebug("Successfully processed push notification for account {AccountId}", notification.AccountId);
    }
}
