using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Wallet.Payment;
using Google.Protobuf;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NodaTime;

namespace DysonNetwork.Wallet.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paymentTask = HandlePaymentEventsAsync(stoppingToken);

        await Task.WhenAll(paymentTask);
    }

    private async Task HandlePaymentEventsAsync(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("payment_events", [PaymentOrderEventBase.Type]);

        var consumer = await js.CreateOrUpdateConsumerAsync("payment_events",
            new ConsumerConfig("wallet_payment_handler"),
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
                    var subscriptions = scope.ServiceProvider.GetRequiredService<Payment.SubscriptionService>();

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
                    var subscriptions = scope.ServiceProvider.GetRequiredService<Payment.SubscriptionService>();

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
                else
                {
                    // Not a subscription or gift order, skip
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
}
