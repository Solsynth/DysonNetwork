using System.Text.Json;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace DysonNetwork.Drive.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var js = new NatsJSContext(nats);
        var stream = await js.GetStreamAsync(AccountDeletedEvent.Type, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("Dy_Drive_AccountDeleted"), cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            AccountDeletedEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data);
                if (evt == null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

                logger.LogInformation("Processing account deletion for: {AccountId}", evt.AccountId);

                using var scope = serviceProvider.CreateScope();
                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken: stoppingToken);
                try
                {
                    var files = await db.Files
                        .Where(p => p.AccountId == evt.AccountId)
                        .ToListAsync(cancellationToken: stoppingToken);

                    if (files.Any())
                    {
                        await fs.DeleteFileDataBatchAsync(files);
                        await db.Files
                            .Where(p => p.AccountId == evt.AccountId)
                            .ExecuteDeleteAsync(cancellationToken: stoppingToken);
                    }

                    await transaction.CommitAsync(cancellationToken: stoppingToken);

                    await msg.AckAsync(cancellationToken: stoppingToken);
                    logger.LogInformation("Account deletion for {AccountId} processed successfully.", evt.AccountId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during transaction for account deletion {AccountId}, rolling back.", evt.AccountId);
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw; // Let outer catch handle Nak
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process account deletion for {AccountId}, will retry.", evt?.AccountId);
                await msg.NakAsync(delay: TimeSpan.FromSeconds(30), cancellationToken: stoppingToken);
            }
        }
    }
}
