using System.Text.Json;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace DysonNetwork.Sphere.Startup;

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
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                await db.ChatMembers
                    .Where(m => m.AccountId == evt.AccountId)
                    .ExecuteDeleteAsync(cancellationToken: stoppingToken);
                
                await db.RealmMembers
                    .Where(m => m.AccountId == evt.AccountId)
                    .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken: stoppingToken);
                try
                {
                    var publishers = await db.Publishers
                        .Where(p => p.Members.All(m => m.AccountId == evt.AccountId))
                        .ToListAsync(cancellationToken: stoppingToken);

                    foreach (var publisher in publishers)
                        await db.Posts
                            .Where(p => p.PublisherId == publisher.Id)
                            .ExecuteDeleteAsync(cancellationToken: stoppingToken);
                    
                    var publisherIds = publishers.Select(p => p.Id).ToList();
                    await db.Publishers
                        .Where(p => publisherIds.Contains(p.Id))
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