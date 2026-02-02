using System.Text.Json;
using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Google.Protobuf;
using NATS.Client.Core;
using NATS.Net;
using NodaTime;

namespace DysonNetwork.Pass.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    private static bool StatusesEqual(SnAccountStatus a, SnAccountStatus b)
    {
        return a.Attitude == b.Attitude &&
               a.IsOnline == b.IsOnline &&
               a.IsCustomized == b.IsCustomized &&
               a.Label == b.Label &&
               a.IsInvisible == b.IsInvisible &&
               a.IsNotDisturb == b.IsNotDisturb;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await HandleWebSocketEventsAsync(stoppingToken);
    }

    private async Task HandleWebSocketEventsAsync(CancellationToken stoppingToken)
    {
        var connectedTask = HandleConnectedEventsAsync(stoppingToken);
        var disconnectedTask = HandleDisconnectedEventsAsync(stoppingToken);

        await Task.WhenAll(connectedTask, disconnectedTask);
    }

    private async Task HandleConnectedEventsAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>("websocket_connected", cancellationToken: stoppingToken))
        {
            try
            {
                var evt =
                    GrpcTypeHelper.ConvertByteStringToObject<WebSocketConnectedEvent>(ByteString.CopyFrom(msg.Data));
                if (evt is null) continue;

                logger.LogInformation("Received WebSocket connected event for user {AccountId}, device {DeviceId}",
                    evt.AccountId, evt.DeviceId);

                await using var scope = serviceProvider.CreateAsyncScope();
                var accountEventService = scope.ServiceProvider.GetRequiredService<AccountEventService>();
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

                var previous = await cache.GetAsync<SnAccountStatus>($"account:status:prev:{evt.AccountId}");
                var status = await accountEventService.GetStatus(evt.AccountId);

                if (previous != null && !StatusesEqual(previous, status))
                {
                    await nats.PublishAsync(
                        AccountStatusUpdatedEvent.Type,
                        ByteString.CopyFromUtf8(JsonSerializer.Serialize(new AccountStatusUpdatedEvent
                        {
                            AccountId = evt.AccountId,
                            Status = status,
                            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                        }, GrpcTypeHelper.SerializerOptionsWithoutIgnore)).ToByteArray(),
                        cancellationToken: stoppingToken
                    );
                }

                logger.LogInformation("Handled status update for user {AccountId} on connect", evt.AccountId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing WebSocket connected event");
            }
        }
    }

    private async Task HandleDisconnectedEventsAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>("websocket_disconnected",
                           cancellationToken: stoppingToken))
        {
            try
            {
                var evt =
                    GrpcTypeHelper.ConvertByteStringToObject<WebSocketDisconnectedEvent>(ByteString.CopyFrom(msg.Data));

                logger.LogInformation(
                    "Received WebSocket disconnected event for user {AccountId}, device {DeviceId}, IsOffline: {IsOffline}",
                    evt.AccountId, evt.DeviceId, evt.IsOffline
                );

                await using var scope = serviceProvider.CreateAsyncScope();
                var accountEventService = scope.ServiceProvider.GetRequiredService<AccountEventService>();
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

                var previous = await cache.GetAsync<SnAccountStatus>($"account:status:prev:{evt.AccountId}");
                var status = await accountEventService.GetStatus(evt.AccountId);

                if (previous != null && !StatusesEqual(previous, status))
                {
                    await nats.PublishAsync(
                        AccountStatusUpdatedEvent.Type,
                        ByteString.CopyFromUtf8(JsonSerializer.Serialize(new AccountStatusUpdatedEvent
                        {
                            AccountId = evt.AccountId,
                            Status = status,
                            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                        }, GrpcTypeHelper.SerializerOptionsWithoutIgnore)).ToByteArray()
                    );
                }

                logger.LogInformation("Handled status update for user {AccountId} on disconnect", evt.AccountId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing WebSocket disconnected event");
            }
        }
    }
}
