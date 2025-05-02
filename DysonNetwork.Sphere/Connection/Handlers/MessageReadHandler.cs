using System.Net.WebSockets;
using DysonNetwork.Sphere.Chat;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Connection.Handlers;

public class MessageReadHandler(AppDatabase db) : IWebSocketPacketHandler
{
    public string PacketType => "message.read";

    public async Task HandleAsync(Account.Account currentUser, string deviceId, WebSocketPacket packet, WebSocket socket)
    {
        var request = packet.GetData<Chat.ChatController.MarkMessageReadRequest>();
        if (request is null)
        {
            await socket.SendAsync(
                new ArraySegment<byte>(new WebSocketPacket
                {
                    Type = WebSocketPacketType.Error,
                    ErrorMessage = "Mark message as read requires you provide the ChatRoomId and MessageId"
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            return;
        }

        var existingStatus = await db.ChatStatuses
            .FirstOrDefaultAsync(x => x.MessageId == request.MessageId && x.Sender.AccountId == currentUser.Id);
        var sender = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == request.ChatRoomId)
            .FirstOrDefaultAsync();
            
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

        if (existingStatus == null)
        {
            existingStatus = new MessageStatus
            {
                MessageId = request.MessageId,
                SenderId = sender.Id,
            };
            db.ChatStatuses.Add(existingStatus);
        }

        await db.SaveChangesAsync();
    }
}