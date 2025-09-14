using System.Text.Json;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DysonNetwork.Drive.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("account_events", [AccountDeletedEvent.Type]);
        
        var consumer = await js.CreateOrUpdateConsumerAsync("account_events",
            new ConsumerConfig("drive_account_deleted_handler"), cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data);
                if (evt == null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

                logger.LogInformation("Account deleted: {AccountId}", evt.AccountId);

                using var scope = serviceProvider.CreateScope();
                var fs = scope.ServiceProvider.GetRequiredService<FileService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken: stoppingToken);
                try
                {
                    var files = await db.Files
                        .Where(p => p.AccountId == evt.AccountId)
                        .ToListAsync(cancellationToken: stoppingToken);

                    await fs.DeleteFileDataBatchAsync(files);
                    await db.Files
                        .Where(p => p.AccountId == evt.AccountId)
                        .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                    await transaction.CommitAsync(cancellationToken: stoppingToken);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken: stoppingToken);
                    throw;
                }

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