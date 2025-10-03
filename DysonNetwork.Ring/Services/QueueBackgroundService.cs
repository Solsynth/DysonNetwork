using System.Text.Json;
using DysonNetwork.Ring.Email;
using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using Google.Protobuf;
using NATS.Client.Core;
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
    private const string QueueGroup = "pusher_workers";
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
        
        await foreach (var msg in nats.SubscribeAsync<byte[]>(QueueName, queueGroup: QueueGroup, cancellationToken: stoppingToken))
        {
            try
            {
                var message = GrpcTypeHelper.ConvertByteStringToObject<QueueMessage>(ByteString.CopyFrom(msg.Data));
                if (message is not null)
                {
                    await ProcessMessageAsync(message, stoppingToken);
                }
                else
                {
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
            emailMessage.Body);
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
        await pushService.DeliverPushNotification(notification, cancellationToken);
        logger.LogDebug("Successfully processed push notification for account {AccountId}", notification.AccountId);
    }
}