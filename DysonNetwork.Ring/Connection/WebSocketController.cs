using System.Net.WebSockets;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NATS.Client.Core;
using NATS.Net;
using Swashbuckle.AspNetCore.Annotations;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Ring.Connection;

[ApiController]
public class WebSocketController(
    WebSocketService ws,
    ILogger<WebSocketContext> logger,
    INatsConnection nats
) : ControllerBase
{
    [Route("/ws")]
    [Authorize]
    [SwaggerIgnore]
    public async Task TheGateway()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        if (currentUserValue is not Account currentUser ||
            currentSessionValue is not AuthSession currentSession)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var accountId = Guid.Parse(currentUser.Id!);
        var deviceId = currentSession.Challenge?.DeviceId ?? Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(deviceId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
            { KeepAliveInterval = TimeSpan.FromSeconds(60) });
        var cts = new CancellationTokenSource();
        var connectionKey = (accountId, deviceId);

        if (!ws.TryAdd(connectionKey, webSocket, cts))
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(new WebSocketPacket
                {
                    Type = "error.dupe",
                    ErrorMessage = "Too many connections from the same device and account."
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            await webSocket.CloseAsync(
                WebSocketCloseStatus.PolicyViolation,
                "Too many connections from the same device and account.",
                CancellationToken.None
            );
            return;
        }

        logger.LogDebug(
            $"Connection established with user @{currentUser.Name}#{currentUser.Id} and device #{deviceId}");

        // Broadcast WebSocket connected event
        await nats.PublishAsync(
            WebSocketConnectedEvent.Type,
            GrpcTypeHelper.ConvertObjectToByteString(new WebSocketConnectedEvent
            {
                AccountId = accountId,
                DeviceId = deviceId,
                IsOffline = false
            }).ToByteArray(),
            cancellationToken: cts.Token
        );

        try
        {
            await _ConnectionEventLoop(deviceId, currentUser, webSocket, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "WebSocket disconnected with user @{UserName}#{UserId} and device #{DeviceId} unexpectedly",
                currentUser.Name,
                currentUser.Id,
                deviceId
            );
        }
        finally
        {
            ws.Disconnect(connectionKey);

            // Broadcast WebSocket disconnected event
            await nats.PublishAsync(
                WebSocketDisconnectedEvent.Type,
                GrpcTypeHelper.ConvertObjectToByteString(new WebSocketDisconnectedEvent
                {
                    AccountId = accountId,
                    DeviceId = deviceId,
                    IsOffline = !WebSocketService.GetAccountIsConnected(accountId)
                }).ToByteArray(),
                cancellationToken: cts.Token
            );

            logger.LogDebug(
                $"Connection disconnected with user @{currentUser.Name}#{currentUser.Id} and device #{deviceId}"
            );
        }
    }

    private async Task _ConnectionEventLoop(
        string deviceId,
        Account currentUser,
        WebSocket webSocket,
        CancellationToken cancellationToken
    )
    {
        var connectionKey = (AccountId: Guid.Parse(currentUser.Id), DeviceId: deviceId);

        var buffer = new byte[1024 * 4];
        try
        {
            while (true)
            {
                var receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken
                );

                if (receiveResult.CloseStatus.HasValue)
                    break;

                var packet = WebSocketPacket.FromBytes(buffer[..receiveResult.Count]);
                await ws.HandlePacket(currentUser, connectionKey.DeviceId, packet, webSocket);
            }
        }
        catch (OperationCanceledException)
        {
            if (
                webSocket.State != WebSocketState.Closed
                && webSocket.State != WebSocketState.Aborted
            )
            {
                ws.Disconnect(connectionKey);
            }
        }
    }
}