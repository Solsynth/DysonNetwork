using System.Text.Json;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Pass.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

                if (evt?.ProductIdentifier is null ||
                    !evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram))
                    continue;

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
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}. Redelivering.", evt?.OrderId);
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
}
