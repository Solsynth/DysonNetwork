using System.Text.Json;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Stream;
using NATS.Client.Core;

namespace DysonNetwork.Pass.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>(PaymentOrderEventBase.Type, cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data);

                if (evt?.ProductIdentifier is null ||
                    !evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram))
                {
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
                    await nats.PublishAsync(PaymentOrderEventBase.Type, msg.Data, cancellationToken: stoppingToken);
                    continue;
                }

                await subscriptions.HandleSubscriptionOrder(order);

                logger.LogInformation("Subscription for order {OrderId} handled successfully.", evt.OrderId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}", evt?.OrderId);
                await nats.PublishAsync(PaymentOrderEventBase.Type, msg.Data, cancellationToken: stoppingToken);
            }
        }
    }
}