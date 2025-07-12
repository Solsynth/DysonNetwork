using System.Net.WebSockets;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace DysonNetwork.Pusher.Connection;

[ApiController]
[Route("/ws")]
public class WebSocketController(WebSocketService ws, ILogger<WebSocketContext> logger) : ControllerBase
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

        var accountId = currentUser.Id!;
        var deviceId = currentSession.Challenge.DeviceId!;

        if (string.IsNullOrEmpty(deviceId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var cts = new CancellationTokenSource();
        var connectionKey = (accountId, deviceId);

        if (!ws.TryAdd(connectionKey, webSocket, cts))
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.InternalServerError,
                "Failed to establish connection.",
                CancellationToken.None
            );
            return;
        }

        logger.LogInformation(
            $"Connection established with user @{currentUser.Name}#{currentUser.Id} and device #{deviceId}");

        try
        {
            await _ConnectionEventLoop(deviceId, currentUser, webSocket, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket Error: {ex.Message}");
        }
        finally
        {
            ws.Disconnect(connectionKey);
            logger.LogInformation(
                $"Connection disconnected with user @{currentUser.Name}#{currentUser.Id} and device #{deviceId}");
        }
    }

    private async Task _ConnectionEventLoop(
        string deviceId,
        Account currentUser,
        WebSocket webSocket,
        CancellationToken cancellationToken
    )
    {
        var connectionKey = (AccountId: currentUser.Id, DeviceId: deviceId);

        var buffer = new byte[1024 * 4];
        try
        {
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken
            );
            while (!receiveResult.CloseStatus.HasValue)
            {
                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken
                );

                var packet = WebSocketPacket.FromBytes(buffer[..receiveResult.Count]);
                _ = ws.HandlePacket(currentUser, connectionKey.DeviceId, packet, webSocket);
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