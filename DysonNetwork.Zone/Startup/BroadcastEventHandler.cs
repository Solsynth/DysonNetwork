using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Zone.Startup;

public class BroadcastEventHandler(
    IServiceProvider serviceProvider,
    ILogger<BroadcastEventHandler> logger,
    INatsConnection nats
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var accountTask = HandleAccountDeletions(stoppingToken);

        await Task.WhenAll(accountTask);
    }

    private async Task HandleAccountDeletions(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("account_events", [AccountDeletedEvent.Type]);

        var consumer = await js.CreateOrUpdateConsumerAsync("account_events",
            new ConsumerConfig("sphere_account_deleted_handler"), cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);
                if (evt == null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

                logger.LogInformation("Account deleted: {AccountId}", evt.AccountId);

                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                // TODO clean up data

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
}
