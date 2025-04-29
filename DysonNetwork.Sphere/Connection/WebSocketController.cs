using System.Collections.Concurrent;
using System.Net.WebSockets;
using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace DysonNetwork.Sphere.Connection;

[ApiController]
[Route("/ws")]
public class WebSocketController : ControllerBase
{
    // Concurrent dictionary to store active WebSocket connections.
    // Key: Tuple (AccountId, DeviceId); Value: WebSocket and CancellationTokenSource
    private static readonly ConcurrentDictionary<
        (long AccountId, string DeviceId),
        (WebSocket Socket, CancellationTokenSource Cts)
    > ActiveConnections =
        new ConcurrentDictionary<(long, string), (WebSocket, CancellationTokenSource)>();

    [Route("/ws")]
    [Authorize]
    [SwaggerIgnore]
    public async Task TheGateway()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            // Get AccountId from HttpContext
            if (
                !HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue)
                || currentUserValue is not Account.Account currentUser
            )
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            long accountId = currentUser.Id;

            // Verify deviceId
            if (string.IsNullOrEmpty(deviceId))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            // Create a CancellationTokenSource for this connection
            var cts = new CancellationTokenSource();
            var connectionKey = (accountId, deviceId);

            // Add the connection to the active connections dictionary
            if (!ActiveConnections.TryAdd(connectionKey, (webSocket, cts)))
            {
                // Failed to add
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.InternalServerError,
                    "Failed to establish connection.",
                    CancellationToken.None
                );
                return;
            }

            try
            {
                await _ConnectionEventLoop(webSocket, connectionKey, cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket Error: {ex.Message}");
            }
            finally
            {
                // Connection is closed, remove it from the active connections dictionary
                ActiveConnections.TryRemove(connectionKey, out _);
                cts.Dispose();
            }
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    private static async Task _ConnectionEventLoop(
        WebSocket webSocket,
        (long AccountId, string DeviceId) connectionKey,
        CancellationToken cancellationToken
    )
    {
        // Buffer for receiving messages.
        var buffer = new byte[1024 * 4];
        try
        {
            // We don't handle receiving data, so we ignore the return.
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken
            );
            while (!receiveResult.CloseStatus.HasValue)
            {
                // Keep connection alive and wait for close requests
                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken
                );
            }

            // Close connection
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            // Connection was canceled, close it gracefully
            if (
                webSocket.State != WebSocketState.Closed
                && webSocket.State != WebSocketState.Aborted
            )
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed by server",
                    CancellationToken.None
                );
            }
        }
    }

    // This method will be used later to send messages to specific connections
    public static async Task SendMessageAsync(long accountId, string deviceId, string message)
    {
        if (ActiveConnections.TryGetValue((accountId, deviceId), out var connection))
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(message);
            await connection.Socket.SendAsync(
                new ArraySegment<byte>(buffer, 0, buffer.Length),
                WebSocketMessageType.Text,
                true,
                connection.Cts.Token
            );
        }
    }
}

