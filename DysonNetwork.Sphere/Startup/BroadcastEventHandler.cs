using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Sphere.Startup;

public class PaymentOrderAwardEvent : PaymentOrderEventBase
{
    public PaymentOrderAwardMeta Meta { get; set; } = null!;
}

public class PaymentOrderAwardMeta
{
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("post_id")] public Guid PostId { get; set; }
    [JsonPropertyName("amount")] public string Amount { get; set; } = null!;
    [JsonPropertyName("attitude")] public Shared.Models.PostReactionAttitude Attitude { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public class BroadcastEventHandler(
    IServiceProvider serviceProvider,
    ILogger<BroadcastEventHandler> logger,
    INatsConnection nats,
    RingService.RingServiceClient pusher
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paymentTask = HandlePaymentOrders(stoppingToken);
        var accountTask = HandleAccountDeletions(stoppingToken);

        await Task.WhenAll(paymentTask, accountTask);
    }

    private async Task HandlePaymentOrders(CancellationToken stoppingToken)
    {
        var js = nats.CreateJetStreamContext();

        await js.EnsureStreamCreated("payment_events", [PaymentOrderEventBase.Type]);

        var consumer = await js.CreateOrUpdateConsumerAsync("payment_events",
            new ConsumerConfig("sphere_payment_handler"), cancellationToken: stoppingToken);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);

                logger.LogInformation(
                    "Received order event: {ProductIdentifier} {OrderId}",
                    evt?.ProductIdentifier,
                    evt?.OrderId
                );

                if (evt?.ProductIdentifier is null)
                    continue;

                switch (evt.ProductIdentifier)
                {
                    case "posts.award":
                        {
                            var awardEvt = JsonSerializer.Deserialize<PaymentOrderAwardEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);
                            if (awardEvt?.Meta == null) throw new ArgumentNullException(nameof(awardEvt));

                            var meta = awardEvt.Meta;

                            logger.LogInformation("Handling post award order: {OrderId}", evt.OrderId);

                            await using var scope = serviceProvider.CreateAsyncScope();
                            var ps = scope.ServiceProvider.GetRequiredService<Post.PostService>();

                            var amountNum = decimal.Parse(meta.Amount);

                            await ps.AwardPost(meta.PostId, meta.AccountId, amountNum, meta.Attitude, meta.Message);

                            logger.LogInformation("Post award for order {OrderId} handled successfully.", evt.OrderId);
                            await msg.AckAsync(cancellationToken: stoppingToken);
                            break;
                        }
                    default:
                        // ignore
                        await msg.AckAsync(cancellationToken: stoppingToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}", evt?.OrderId);
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
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

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }

    private async Task SendErrorResponse(WebSocketPacketEvent evt, string message)
    {
        await pusher.PushWebSocketPacketToDeviceAsync(new PushWebSocketPacketToDeviceRequest
        {
            DeviceId = evt.DeviceId,
            Packet = new WebSocketPacket
            {
                Type = "error",
                ErrorMessage = message
            }.ToProtoValue()
        });
    }
}
