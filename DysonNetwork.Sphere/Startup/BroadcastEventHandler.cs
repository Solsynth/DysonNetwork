using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Stream;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace DysonNetwork.Sphere.Startup;

public class PaymentOrderAwardEvent : PaymentOrderEventBase
{
    public PaymentOrderAwardMeta Meta { get; set; } = null!;
}

public class PaymentOrderAwardMeta
{
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("post_id")] public Guid PostId { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; }
    [JsonPropertyName("attitude")] public PostReactionAttitude Attitude { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

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

                // Every order goes into the MQ is already paid, so we skipped the status validation

                if (evt?.ProductIdentifier is null)
                    continue;

                switch (evt.ProductIdentifier)
                {
                    case "posts.award":
                    {
                        var awardEvt = JsonSerializer.Deserialize<PaymentOrderAwardEvent>(msg.Data);
                        if (awardEvt?.Meta == null) throw new ArgumentNullException(nameof(awardEvt));
                        
                        var meta = awardEvt.Meta;
                        
                        logger.LogInformation("Handling post award order: {OrderId}", evt.OrderId);

                        await using var scope = serviceProvider.CreateAsyncScope();
                        var ps = scope.ServiceProvider.GetRequiredService<PostService>();

                        var amountNum = decimal.Parse(meta.Amount);

                        await ps.AwardPost(meta.PostId, meta.AccountId, amountNum, meta.Attitude, meta.Message);

                        logger.LogInformation("Post award for order {OrderId} handled successfully.", evt.OrderId);

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}", evt?.OrderId);
            }
        }

        await foreach (var msg in nats.SubscribeAsync<byte[]>(AccountDeletedEvent.Type,
                           cancellationToken: stoppingToken))
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