using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Chat;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Sphere.Connection;

public class WebSocketHandlerGrpc(RingService.RingServiceClient pusher, ChatRoomService crs, ChatService cs)
    : RingHandlerService.RingHandlerServiceBase
{
    public override async Task<Empty> ReceiveWebSocketPacket(
        ReceiveWebSocketPacketRequest request,
        ServerCallContext context
    )
    {
        switch (request.Packet.Type)
        {
            case "messages.read":
                await HandleMessageRead(request, context);
                break;
            case "messages.typing":
                await HandleMessageTyping(request, context);
                break;
        }

        return new Empty();
    }

    private async Task HandleMessageRead(ReceiveWebSocketPacketRequest request, ServerCallContext context)
    {
        var currentUser = request.Account;
        var packet = request.Packet;

        if (packet.Data == null)
        {
            await SendErrorResponse(
                request,
                "Mark message as read requires you to provide the ChatRoomId"
            );
            return;
        }

        var requestData = GrpcTypeHelper.ConvertByteStringToObject<ChatController.MarkMessageReadRequest>(packet.Data);
        if (requestData == null)
        {
            await SendErrorResponse(request, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(
            Guid.Parse(currentUser.Id),
            requestData.ChatRoomId
        );

        if (sender == null)
        {
            await SendErrorResponse(request, "User is not a member of the chat room.");
            return;
        }
        
        await cs.ReadChatRoomAsync(requestData.ChatRoomId, Guid.Parse(currentUser.Id));
    }

    private async Task HandleMessageTyping(ReceiveWebSocketPacketRequest request, ServerCallContext context)
    {
        var currentUser = request.Account;
        var packet = request.Packet;

        if (packet.Data == null)
        {
            await SendErrorResponse(request, "messages.typing requires you to provide the ChatRoomId");
            return;
        }

        var requestData = GrpcTypeHelper.ConvertByteStringToObject<ChatController.ChatRoomWsUniversalRequest>(packet.Data);
        if (requestData == null)
        {
            await SendErrorResponse(request, "Invalid request data");
            return;
        }

        var sender = await crs.GetRoomMember(
            Guid.Parse(currentUser.Id),
            requestData.ChatRoomId
        );
        if (sender == null)
        {
            await SendErrorResponse(request, "User is not a member of the chat room.");
            return;
        }

        var responsePacket = new WebSocketPacket
        {
            Type = "messages.typing",
            Data = GrpcTypeHelper.ConvertObjectToByteString(new
            {
                room_id = sender.ChatRoomId,
                sender_id = sender.Id,
                sender = sender
            })
        };

        // Broadcast typing indicator to other room members
        var otherMembers = (await crs.ListRoomMembers(requestData.ChatRoomId))
            .Where(m => m.AccountId != Guid.Parse(currentUser.Id))
            .Select(m => m.AccountId.ToString())
            .ToList();

        var respRequest = new PushWebSocketPacketToUsersRequest() { Packet = responsePacket };
        respRequest.UserIds.AddRange(otherMembers);

        await pusher.PushWebSocketPacketToUsersAsync(respRequest);
    }

    private async Task SendErrorResponse(ReceiveWebSocketPacketRequest request, string message)
    {
        await pusher.PushWebSocketPacketToDeviceAsync(new PushWebSocketPacketToDeviceRequest
        {
            DeviceId = request.DeviceId,
            Packet = new WebSocketPacket
            {
                Type = "error",
                ErrorMessage = message
            }
        });
    }
}