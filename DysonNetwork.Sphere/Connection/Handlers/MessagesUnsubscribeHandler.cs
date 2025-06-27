using System.Net.WebSockets;
using DysonNetwork.Sphere.Chat;

namespace DysonNetwork.Sphere.Connection.Handlers;

public class MessagesUnsubscribeHandler() : IWebSocketPacketHandler
{
    public string PacketType => "messages.unsubscribe";

    public Task HandleAsync(
        Account.Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket,
        WebSocketService srv
    )
    {
        srv.UnsubscribeFromChatRoom(deviceId);
        return Task.CompletedTask;
    }
}
