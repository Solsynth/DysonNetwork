using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DysonNetwork.Sphere.Connection;

public class WebSocketService
{
    private readonly IDictionary<string, IWebSocketPacketHandler> _handlerMap;

    public WebSocketService(IEnumerable<IWebSocketPacketHandler> handlers)
    {
        _handlerMap = handlers.ToDictionary(h => h.PacketType);
    }

    private static readonly ConcurrentDictionary<
        (Guid AccountId, string DeviceId),
        (WebSocket Socket, CancellationTokenSource Cts)
    > ActiveConnections = new();

    public bool TryAdd(
        (Guid AccountId, string DeviceId) key,
        WebSocket socket,
        CancellationTokenSource cts
    )
    {
        if (ActiveConnections.TryGetValue(key, out _))
            Disconnect(key,
                "Just connected somewhere else with the same identifier."); // Disconnect the previous one using the same identifier
        return ActiveConnections.TryAdd(key, (socket, cts));
    }

    public void Disconnect((Guid AccountId, string DeviceId) key, string? reason = null)
    {
        if (!ActiveConnections.TryGetValue(key, out var data)) return;
        data.Socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            reason ?? "Server just decided to disconnect.",
            CancellationToken.None
        );
        data.Cts.Cancel();
        ActiveConnections.TryRemove(key, out _);
    }

    public bool GetAccountIsConnected(Guid accountId)
    {
        return ActiveConnections.Any(c => c.Key.AccountId == accountId);
    }

    public void SendPacketToAccount(Guid userId, WebSocketPacket packet)
    {
        var connections = ActiveConnections.Where(c => c.Key.AccountId == userId);
        var packetBytes = packet.ToBytes();
        var segment = new ArraySegment<byte>(packetBytes);

        foreach (var connection in connections)
        {
            connection.Value.Socket.SendAsync(
                segment,
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
        }
    }

    public void SendPacketToDevice(string deviceId, WebSocketPacket packet)
    {
        var connections = ActiveConnections.Where(c => c.Key.DeviceId == deviceId);
        var packetBytes = packet.ToBytes();
        var segment = new ArraySegment<byte>(packetBytes);

        foreach (var connection in connections)
        {
            connection.Value.Socket.SendAsync(
                segment,
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
        }
    }

    public async Task HandlePacket(Account.Account currentUser, string deviceId, WebSocketPacket packet,
        WebSocket socket)
    {
        if (_handlerMap.TryGetValue(packet.Type, out var handler))
        {
            await handler.HandleAsync(currentUser, deviceId, packet, socket);
            return;
        }

        await socket.SendAsync(
            new ArraySegment<byte>(new WebSocketPacket
            {
                Type = WebSocketPacketType.Error,
                ErrorMessage = $"Unprocessable packet: {packet.Type}"
            }.ToBytes()),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None
        );
    }
}