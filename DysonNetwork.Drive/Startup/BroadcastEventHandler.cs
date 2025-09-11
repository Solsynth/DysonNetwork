using System.Text.Json;
using DysonNetwork.Drive.Storage;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace DysonNetwork.Drive.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>(AccountDeletedEvent.Type, cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data);
                if (evt == null) continue;

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
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
            }
        }
    }
}
