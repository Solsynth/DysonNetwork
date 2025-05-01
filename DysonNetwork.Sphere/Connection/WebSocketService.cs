using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DysonNetwork.Sphere.Connection;

public class WebSocketService
{
    public static readonly ConcurrentDictionary<
        (long AccountId, string DeviceId),
        (WebSocket Socket, CancellationTokenSource Cts)
    > ActiveConnections = new();

    public bool TryAdd(
        (long AccountId, string DeviceId) key,
        WebSocket socket,
        CancellationTokenSource cts
    )
    {
        if (ActiveConnections.TryGetValue(key, out _))
            Disconnect(key, "Just connected somewhere else with the same identifier."); // Disconnect the previous one using the same identifier
        return ActiveConnections.TryAdd(key, (socket, cts));
    }

    public void Disconnect((long AccountId, string DeviceId) key, string? reason = null)
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
}