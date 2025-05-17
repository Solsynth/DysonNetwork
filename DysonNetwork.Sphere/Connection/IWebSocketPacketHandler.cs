using System.Net.WebSockets;

namespace DysonNetwork.Sphere.Connection;

public interface IWebSocketPacketHandler
{
    string PacketType { get; }
    Task HandleAsync(Account.Account currentUser, string deviceId, WebSocketPacket packet, WebSocket socket, WebSocketService srv);
}