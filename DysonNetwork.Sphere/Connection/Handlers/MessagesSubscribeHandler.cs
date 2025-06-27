
using System.Net.WebSockets;
using DysonNetwork.Sphere.Chat;

namespace DysonNetwork.Sphere.Connection.Handlers;

public class MessagesSubscribeHandler(ChatRoomService crs, WebSocketService webSocketService) : IWebSocketPacketHandler
{
    public string PacketType => "messages.subscribe";

    public async Task HandleAsync(
        Account.Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket,
        WebSocketService srv
    )
    {
        var request = packet.GetData<ChatController.TypingMessageRequest>();
        if (request is null)
        {
            await socket.SendAsync(
                new ArraySegment<byte>(new WebSocketPacket
                {
                    Type = WebSocketPacketType.Error,
                    ErrorMessage = "messages.subscribe requires you provide the ChatRoomId"
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            return;
        }

        var sender = await crs.GetRoomMember(currentUser.Id, request.ChatRoomId);
        if (sender is null)
        {
            await socket.SendAsync(
                new ArraySegment<byte>(new WebSocketPacket
                {
                    Type = WebSocketPacketType.Error,
                    ErrorMessage = "User is not a member of the chat room."
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            return;
        }

        webSocketService.SubscribeToChatRoom(sender.ChatRoomId.ToString(), deviceId);
    }
}
