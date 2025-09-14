using System.Net.WebSockets;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Ring.Connection;

public interface IWebSocketPacketHandler
{
    string PacketType { get; }

    Task HandleAsync(
        Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket,
        WebSocketService srv
    );
}