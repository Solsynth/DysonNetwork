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
        await foreach (var msg in nats.SubscribeAsync<byte[]>(PaymentOrderEvent.Type, cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data);

                if (evt?.ProductIdentifier is null || !evt.ProductIdentifier.StartsWith(SubscriptionType.StellarProgram))
                    continue;
                
                logger.LogInformation("Stellar program order paid: {OrderId}", evt.OrderId);
                
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
            }
        }
    }
}