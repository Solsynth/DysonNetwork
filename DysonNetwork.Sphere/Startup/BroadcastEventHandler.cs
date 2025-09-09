using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Stream;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

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
        try
        {
            var paymentTask = ProcessPaymentOrdersAsync(stoppingToken);
            var accountTask = ProcessAccountDeletionsAsync(stoppingToken);

            await Task.WhenAll(paymentTask, accountTask);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BroadcastEventHandler stopped due to an unhandled exception.");
        }
    }

    private async Task ProcessPaymentOrdersAsync(CancellationToken stoppingToken)
    {
        var js = new NatsJSContext(nats);
        var stream = await js.GetStreamAsync(PaymentOrderEventBase.Type, cancellationToken: stoppingToken);
        var consumer = await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("DysonNetwork_Sphere_PaymentOrder"),
            cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data);

                if (evt?.ProductIdentifier is null)
                {
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    continue;
                }

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

                        await msg.AckAsync(cancellationToken: stoppingToken);
                        break;
                    }
                    default:
                        // Not for us, acknowledge and ignore.
                        await msg.AckAsync(cancellationToken: stoppingToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}, will retry.",
                    evt?.OrderId);
                await msg.NakAsync(delay: TimeSpan.FromSeconds(30), cancellationToken: stoppingToken);
            }
        }
    }

    private async Task ProcessAccountDeletionsAsync(CancellationToken stoppingToken)
    {
        var js = new NatsJSContext(nats);
        var stream = await js.GetStreamAsync(AccountDeletedEvent.Type, cancellationToken: stoppingToken);
        var consumer =
            await stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("DysonNetwork_Sphere_AccountDeleted"),
                cancellationToken: stoppingToken);

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

                    if (publishers.Any())
                    {
                        foreach (var publisher in publishers)
                        {
                            await db.Posts
                                .Where(p => p.PublisherId == publisher.Id)
                                .ExecuteDeleteAsync(cancellationToken: stoppingToken);
                        }

                        var publisherIds = publishers.Select(p => p.Id).ToList();
                        await db.Publishers
                            .Where(p => publisherIds.Contains(p.Id))
                            .ExecuteDeleteAsync(cancellationToken: stoppingToken);
                    }

                    await transaction.CommitAsync(cancellationToken: stoppingToken);
                    await msg.AckAsync(cancellationToken: stoppingToken);
                    logger.LogInformation("Account deletion for {AccountId} processed successfully in Sphere.",
                        evt.AccountId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error during transaction for account deletion {AccountId} in Sphere, rolling back.",
                        evt.AccountId);
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process account deletion for {AccountId} in Sphere, will retry.",
                    evt?.AccountId);
                await msg.NakAsync(delay: TimeSpan.FromSeconds(30), cancellationToken: stoppingToken);
            }
        }
    }
}