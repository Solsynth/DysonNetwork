using System.Text.Json;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Stream;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace DysonNetwork.Pass.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var js = new NatsJSContext(nats);
        var stream = await js.GetStreamAsync(PaymentOrderEventBase.Type, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("Dy_Pass_Stellar"), cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data);

                if (evt?.ProductIdentifier is null ||
                    !evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram))
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

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
                    logger.LogWarning("Order with ID {OrderId} not found.", evt.OrderId);
                    await msg.NakAsync(delay: TimeSpan.FromSeconds(30), cancellationToken: stoppingToken);
                    continue;
                }

                await subscriptions.HandleSubscriptionOrder(order);

                logger.LogInformation("Subscription for order {OrderId} handled successfully.", evt.OrderId);
                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}", evt?.OrderId);
                await msg.NakAsync(delay: TimeSpan.FromSeconds(30), cancellationToken: stoppingToken);
            }
        }
    }
}
