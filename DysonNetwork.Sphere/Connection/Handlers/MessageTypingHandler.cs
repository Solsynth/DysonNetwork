using System.Net.WebSockets;
using DysonNetwork.Sphere.Chat;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Connection.Handlers;

public class MessageTypingHandler(AppDatabase db, ChatRoomService crs, ICacheService cache) : IWebSocketPacketHandler
{
    public string PacketType => "messages.typing";

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
                    ErrorMessage = "Mark message as read requires you provide the ChatRoomId"
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            return;
        }

        var sender = await crs.GetChannelMember(currentUser.Id, request.ChatRoomId);
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

        // Broadcast read statuses
        var otherMembers = (await crs.ListRoomMembers(request.ChatRoomId)).Select(m => m.AccountId).ToList();
        foreach (var member in otherMembers)
            srv.SendPacketToAccount(member, packet);
    }
}