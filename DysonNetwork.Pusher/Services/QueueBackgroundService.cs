using System.Text.Json;
using DysonNetwork.Pusher.Email;
using DysonNetwork.Pusher.Notification;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Google.Protobuf;
using NATS.Client.Core;

namespace DysonNetwork.Pusher.Services;

public class QueueBackgroundService(
    INatsConnection nats,
    IServiceProvider serviceProvider,
    ILogger<QueueBackgroundService> logger,
    IConfiguration configuration
)
    : BackgroundService
{
    public const string QueueName = "pusher.queue";
    public const string QueueGroup = "pusher.workers";
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
        logger.LogInformation("Queue consumer started");

        await foreach (var msg in nats.SubscribeAsync<byte[]>(
                           QueueName,
                           queueGroup: QueueGroup,
                           cancellationToken: stoppingToken))
        {
            try
            {
                var message = GrpcTypeHelper.ConvertByteStringToObject<QueueMessage>(ByteString.CopyFrom(msg.Data));
                if (message is not null)
                {
                    await ProcessMessageAsync(msg, message, stoppingToken);
                }
                else
                {
                    await msg.ReplyAsync(cancellationToken: stoppingToken);
                    logger.LogWarning($"Invalid message format for {msg.Subject}");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in queue consumer");
                // Add a small delay to prevent tight error loops
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async ValueTask ProcessMessageAsync(NatsMsg<byte[]> rawMsg, QueueMessage message,
        CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();

        logger.LogDebug("Processing message of type {MessageType}", message.Type);

        try
        {
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

            await rawMsg.ReplyAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message of type {MessageType}", message.Type);
            // Don't rethrow to prevent the message from being retried indefinitely
            // In a production scenario, you might want to implement a dead-letter queue
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
            emailMessage.Body);
    }

    private static async Task ProcessPushNotificationMessageAsync(QueueMessage message, IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var pushService = scope.ServiceProvider.GetRequiredService<PushService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<QueueBackgroundService>>();

        var notification = JsonSerializer.Deserialize<Notification.Notification>(message.Data);
        if (notification == null)
        {
            logger.LogError("Invalid push notification data format");
            return;
        }

        try
        {
            logger.LogDebug("Processing push notification for account {AccountId}", notification.AccountId);
            await pushService.DeliverPushNotification(notification, cancellationToken);
            logger.LogDebug("Successfully processed push notification for account {AccountId}", notification.AccountId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing push notification for account {AccountId}", notification.AccountId);
            // Don't rethrow to prevent the message from being retried indefinitely
        }
    }
}