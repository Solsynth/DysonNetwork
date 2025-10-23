using System.Text.Json;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using Google.Protobuf;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NodaTime;

namespace DysonNetwork.Pass.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paymentTask = HandlePaymentEventsAsync(stoppingToken);
        var webSocketTask = HandleWebSocketEventsAsync(stoppingToken);

        await Task.WhenAll(paymentTask, webSocketTask);
    }

    private async Task HandlePaymentEventsAsync(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("payment_events", [PaymentOrderEventBase.Type]);

        var consumer = await js.CreateOrUpdateConsumerAsync("payment_events",
            new ConsumerConfig("pass_payment_handler"),
            cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);

                logger.LogInformation(
                    "Received order event: {ProductIdentifier} {OrderId}",
                    evt?.ProductIdentifier,
                    evt?.OrderId
                );

                if (evt?.ProductIdentifier is null)
                    continue;

                // Handle subscription orders
                if (
                    evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram) &&
                    evt.Meta?.TryGetValue("gift_id", out var giftIdValue) == true
                )
                {
                    logger.LogInformation("Handling gift order: {OrderId}", evt.OrderId);

                    await using var scope = serviceProvider.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
                    var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

                    var order = await db.PaymentOrders.FindAsync(
                        [evt.OrderId],
                        cancellationToken: stoppingToken
                    );
                    if (order is null)
                    {
                        logger.LogWarning("Order with ID {OrderId} not found. Redelivering.", evt.OrderId);
                        await msg.NakAsync(cancellationToken: stoppingToken);
                        continue;
                    }

                    await subscriptions.HandleGiftOrder(order);

                    logger.LogInformation("Gift for order {OrderId} handled successfully.", evt.OrderId);
                    await msg.AckAsync(cancellationToken: stoppingToken);
                }
                else if (evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram))
                {
                    logger.LogInformation("Handling stellar program order: {OrderId}", evt.OrderId);

                    await using var scope = serviceProvider.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
                    var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

                    var order = await db.PaymentOrders.FindAsync(
                        [evt.OrderId],
                        cancellationToken: stoppingToken
                    );
                    if (order is null)
                    {
                        logger.LogWarning("Order with ID {OrderId} not found. Redelivering.", evt.OrderId);
                        await msg.NakAsync(cancellationToken: stoppingToken);
                        continue;
                    }

                    await subscriptions.HandleSubscriptionOrder(order);

                    logger.LogInformation("Subscription for order {OrderId} handled successfully.", evt.OrderId);
                    await msg.AckAsync(cancellationToken: stoppingToken);
                }
                else if (evt.ProductIdentifier == "lottery")
                {
                    logger.LogInformation("Handling lottery order: {OrderId}", evt.OrderId);

                    await using var scope = serviceProvider.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
                    var lotteries = scope.ServiceProvider.GetRequiredService<Lotteries.LotteryService>();

                    var order = await db.PaymentOrders.FindAsync(
                        [evt.OrderId],
                        cancellationToken: stoppingToken
                    );
                    if (order == null)
                    {
                        logger.LogWarning("Order with ID {OrderId} not found. Redelivering.", evt.OrderId);
                        await msg.NakAsync(cancellationToken: stoppingToken);
                        continue;
                    }

                    await lotteries.HandleLotteryOrder(order);

                    logger.LogInformation("Lottery ticket for order {OrderId} created successfully.", evt.OrderId);
                    await msg.AckAsync(cancellationToken: stoppingToken);
                }
                else
                {
                    // Not a subscription, gift, or lottery order, skip
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}. Redelivering.",
                    evt?.OrderId);
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }

    private async Task HandleWebSocketEventsAsync(CancellationToken stoppingToken)
    {
        var connectedTask = HandleConnectedEventsAsync(stoppingToken);
        var disconnectedTask = HandleDisconnectedEventsAsync(stoppingToken);

        await Task.WhenAll(connectedTask, disconnectedTask);
    }

    private async Task HandleConnectedEventsAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>("websocket_connected", cancellationToken: stoppingToken))
        {
            try
            {
                var evt =
                    GrpcTypeHelper.ConvertByteStringToObject<WebSocketConnectedEvent>(ByteString.CopyFrom(msg.Data));

                logger.LogInformation("Received WebSocket connected event for user {AccountId}, device {DeviceId}",
                    evt.AccountId, evt.DeviceId);

                await using var scope = serviceProvider.CreateAsyncScope();
                var accountEventService = scope.ServiceProvider.GetRequiredService<AccountEventService>();

                var status = await accountEventService.GetStatus(evt.AccountId);

                await nats.PublishAsync(
                    AccountStatusUpdatedEvent.Type,
                    GrpcTypeHelper.ConvertObjectToByteString(new AccountStatusUpdatedEvent
                    {
                        AccountId = evt.AccountId,
                        Status = status,
                        UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                    }).ToByteArray()
                );

                logger.LogInformation("Broadcasted status update for user {AccountId}", evt.AccountId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing WebSocket connected event");
            }
        }
    }

    private async Task HandleDisconnectedEventsAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>("websocket_disconnected",
                           cancellationToken: stoppingToken))
        {
            try
            {
                var evt =
                    GrpcTypeHelper.ConvertByteStringToObject<WebSocketDisconnectedEvent>(ByteString.CopyFrom(msg.Data));

                logger.LogInformation(
                    "Received WebSocket disconnected event for user {AccountId}, device {DeviceId}, IsOffline: {IsOffline}",
                    evt.AccountId, evt.DeviceId, evt.IsOffline
                );

                await using var scope = serviceProvider.CreateAsyncScope();
                var accountEventService = scope.ServiceProvider.GetRequiredService<AccountEventService>();

                var status = await accountEventService.GetStatus(evt.AccountId);

                await nats.PublishAsync(
                    AccountStatusUpdatedEvent.Type,
                    GrpcTypeHelper.ConvertObjectToByteString(new AccountStatusUpdatedEvent
                    {
                        AccountId = evt.AccountId,
                        Status = status,
                        UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                    }).ToByteArray()
                );

                logger.LogInformation("Broadcasted status update for user {AccountId}", evt.AccountId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing WebSocket disconnected event");
            }
        }
    }
}
