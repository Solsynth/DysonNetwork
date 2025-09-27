using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Post;
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
    [JsonPropertyName("attitude")] public PostReactionAttitude Attitude { get; set; }
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
        var websocketTask = HandleWebSocketPackets(stoppingToken);

        await Task.WhenAll(paymentTask, accountTask, websocketTask);
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
                            var ps = scope.ServiceProvider.GetRequiredService<PostService>();

                            var amountNum = decimal.Parse(meta.Amount);

                            await ps.AwardPost(meta.PostId, meta.AccountId, amountNum, meta.Attitude, meta.Message);

                            logger.LogInformation("Post award for order {OrderId} handled successfully.", evt.OrderId);
                            await msg.AckAsync(cancellationToken: stoppingToken);
                            break;
                        }
                    default:
                        await msg.NakAsync(cancellationToken: stoppingToken);
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

                await msg.AckAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
                await msg.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }

    private async Task HandleWebSocketPackets(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>(
                           WebSocketPacketEvent.SubjectPrefix + "sphere", cancellationToken: stoppingToken))
        {
            logger.LogDebug("Handling websocket packet...");

            try
            {
                var evt = JsonSerializer.Deserialize<WebSocketPacketEvent>(msg.Data, GrpcTypeHelper.SerializerOptions);
                if (evt == null) throw new ArgumentNullException(nameof(evt));
                var packet = WebSocketPacket.FromBytes(evt.PacketBytes);
                logger.LogInformation("Handling websocket packet... {Type}", packet.Type);
                switch (packet.Type)
                {
                    case "messages.read":
                        await HandleMessageRead(evt, packet);
                        break;
                    case "messages.typing":
                        await HandleMessageTyping(evt, packet);
                        break;
                    case "messages.subscribe":
                        await HandleMessageSubscribe(evt, packet);
                        break;
                    case "messages.unsubscribe":
                        await HandleMessageUnsubscribe(evt, packet);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing websocket packet");
            }
        }
    }

    private async Task HandleMessageRead(WebSocketPacketEvent evt, WebSocketPacket packet)
    {
        using var scope = serviceProvider.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<ChatService>();
        var crs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        if (packet.Data == null)
        {
            await SendErrorResponse(evt, "Mark message as read requires you to provide the ChatRoomId");
            return;
        }

        var requestData = packet.GetData<ChatController.MarkMessageReadRequest>();
        if (requestData == null)
        {
            await SendErrorResponse(evt, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
        if (sender == null)
        {
            await SendErrorResponse(evt, "User is not a member of the chat room.");
            return;
        }

        await cs.ReadChatRoomAsync(requestData.ChatRoomId, evt.AccountId);
    }

    private async Task HandleMessageTyping(WebSocketPacketEvent evt, WebSocketPacket packet)
    {
        using var scope = serviceProvider.CreateScope();
        var crs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        if (packet.Data == null)
        {
            await SendErrorResponse(evt, "messages.typing requires you to provide the ChatRoomId");
            return;
        }

        var requestData = packet.GetData<ChatController.ChatRoomWsUniversalRequest>();
        if (requestData == null)
        {
            await SendErrorResponse(evt, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
        if (sender == null)
        {
            await SendErrorResponse(evt, "User is not a member of the chat room.");
            return;
        }

        var responsePacket = new WebSocketPacket
        {
            Type = "messages.typing",
            Data = new
            {
                room_id = sender.ChatRoomId,
                sender_id = sender.Id,
                sender = sender
            }
        };

        // Broadcast typing indicator to other room members
        var otherMembers = (await crs.ListRoomMembers(requestData.ChatRoomId))
            .Where(m => m.AccountId != evt.AccountId)
            .Select(m => m.AccountId.ToString())
            .ToList();

        var respRequest = new PushWebSocketPacketToUsersRequest() { Packet = responsePacket.ToProtoValue() };
        respRequest.UserIds.AddRange(otherMembers);

        await pusher.PushWebSocketPacketToUsersAsync(respRequest);
    }

    private async Task HandleMessageSubscribe(WebSocketPacketEvent evt, WebSocketPacket packet)
    {
        using var scope = serviceProvider.CreateScope();
        var crs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        if (packet.Data == null)
        {
            await SendErrorResponse(evt, "messages.subscribe requires you to provide the ChatRoomId");
            return;
        }

        var requestData = packet.GetData<ChatController.ChatRoomWsUniversalRequest>();
        if (requestData == null)
        {
            await SendErrorResponse(evt, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
        if (sender == null)
        {
            await SendErrorResponse(evt, "User is not a member of the chat room.");
            return;
        }

        await crs.SubscribeChatRoom(sender);
    }

    private async Task HandleMessageUnsubscribe(WebSocketPacketEvent evt, WebSocketPacket packet)
    {
        using var scope = serviceProvider.CreateScope();
        var crs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        if (packet.Data == null)
        {
            await SendErrorResponse(evt, "messages.unsubscribe requires you to provide the ChatRoomId");
            return;
        }

        var requestData = packet.GetData<ChatController.ChatRoomWsUniversalRequest>();
        if (requestData == null)
        {
            await SendErrorResponse(evt, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(evt.AccountId, requestData.ChatRoomId);
        if (sender == null)
        {
            await SendErrorResponse(evt, "User is not a member of the chat room.");
            return;
        }

        await crs.UnsubscribeChatRoom(sender);
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
