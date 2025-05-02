using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DysonNetwork.Sphere.Connection;

public class WebSocketService(ChatService cs)
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

    public void HandlePacket(Account.Account currentUser, string deviceId, WebSocketPacket packet, WebSocket socket)
    {
        switch (packet.Type)
        {
            case "message.read":
                var request = packet.GetData<ChatController.MarkMessageReadRequest>();
                if (request is null)
                {
                    socket.SendAsync(
                        new ArraySegment<byte>(new WebSocketPacket
                        {
                            Type = WebSocketPacketType.Error,
                            ErrorMessage = "Mark message as read requires you provide the ChatRoomId and MessageId"
                        }.ToBytes()),
                        WebSocketMessageType.Binary,
                        true,
                        CancellationToken.None
                    );
                    break;
                }
                _ = cs.MarkMessageAsReadAsync(request.MessageId, currentUser.Id, currentUser.Id).ConfigureAwait(false);
                break;
            default:
                socket.SendAsync(
                    new ArraySegment<byte>(new WebSocketPacket
                    {
                        Type = WebSocketPacketType.Error,
                        ErrorMessage = $"Unprocessable packet: {packet.Type}"
                    }.ToBytes()),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None
                );
                break;
        }
    }
}