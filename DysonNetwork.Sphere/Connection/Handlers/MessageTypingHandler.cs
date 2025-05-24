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

        var cacheKey = string.Format(MessageReadHandler.ChatMemberCacheKey, currentUser.Id, request.ChatRoomId);
        var sender = await cache.GetAsync<ChatMember?>(cacheKey);
        if (sender is null)
        {
            sender = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == request.ChatRoomId)
                .FirstOrDefaultAsync();

            if (sender != null)
            {
                var chatRoomGroup = ChatRoomService.ChatRoomGroupPrefix + request.ChatRoomId;
                await cache.SetWithGroupsAsync(cacheKey, sender,
                    [chatRoomGroup], 
                    TimeSpan.FromMinutes(5));
            }
        }

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