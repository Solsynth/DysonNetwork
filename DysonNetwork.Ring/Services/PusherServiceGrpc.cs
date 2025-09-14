using DysonNetwork.Ring.Connection;
using DysonNetwork.Ring.Email;
using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Text.Json;

namespace DysonNetwork.Ring.Services;

public class RingServiceGrpc(
    QueueService queueService,
    WebSocketService websocket,
    PushService pushService
) : RingService.RingServiceBase
{
    public override async Task<Empty> SendEmail(SendEmailRequest request, ServerCallContext context)
    {
        await queueService.EnqueueEmail(
            request.Email.ToName,
            request.Email.ToAddress,
            request.Email.Subject,
            request.Email.Body
        );
        return new Empty();
    }

    public override Task<Empty> PushWebSocketPacket(PushWebSocketPacketRequest request, ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Packet.Data),
            ErrorMessage = request.Packet.ErrorMessage
        };
        websocket.SendPacketToAccount(request.UserId, packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToUsers(PushWebSocketPacketToUsersRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Packet.Data),
            ErrorMessage = request.Packet.ErrorMessage
        };
        
        foreach (var userId in request.UserIds)
        {
            websocket.SendPacketToAccount(userId, packet);
        }
        
        return Task.FromResult(new Empty());
    }

public override Task<Empty> PushWebSocketPacketToDevice(PushWebSocketPacketToDeviceRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Packet.Data),
            ErrorMessage = request.Packet.ErrorMessage
        };
        websocket.SendPacketToDevice(request.DeviceId, packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToDevices(PushWebSocketPacketToDevicesRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Packet.Data),
            ErrorMessage = request.Packet.ErrorMessage
        };
        
        foreach (var deviceId in request.DeviceIds)
        {
            websocket.SendPacketToDevice(deviceId, packet);
        }
        
        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> SendPushNotificationToUser(SendPushNotificationToUserRequest request,
        ServerCallContext context)
    {
        var notification = new Notification.Notification
        {
            Topic = request.Notification.Topic,
            Title = request.Notification.Title,
            Subtitle = request.Notification.Subtitle,
            Content = request.Notification.Body,
            Meta = request.Notification.HasMeta
                ? GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Notification.Meta) ?? []
                : [],
            AccountId = Guid.Parse(request.UserId),
        };
        
        if (request.Notification.ActionUri is not null)
            notification.Meta["action_uri"] = request.Notification.ActionUri;

        if (request.Notification.IsSavable)
            await pushService.SaveNotification(notification);
            
        await queueService.EnqueuePushNotification(
            notification,
            Guid.Parse(request.UserId),
            request.Notification.IsSavable
        );
        
        return new Empty();
    }

    public override async Task<Empty> SendPushNotificationToUsers(SendPushNotificationToUsersRequest request,
        ServerCallContext context)
    {
        var notification = new Notification.Notification
        {
            Topic = request.Notification.Topic,
            Title = request.Notification.Title,
            Subtitle = request.Notification.Subtitle,
            Content = request.Notification.Body,
            Meta = request.Notification.HasMeta
                ? GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Notification.Meta) ?? []
                : [],
        };
        
        if (request.Notification.ActionUri is not null)
            notification.Meta["action_uri"] = request.Notification.ActionUri;

        var userIds = request.UserIds.Select(Guid.Parse).ToList();
        if (request.Notification.IsSavable)
            await pushService.SaveNotification(notification, userIds);
            
        var tasks = userIds
            .Select(userId => queueService.EnqueuePushNotification(
                notification,
                userId,
                request.Notification.IsSavable
            ));
            
        await Task.WhenAll(tasks);
        return new Empty();
    }

    public override async Task<Empty> UnsubscribePushNotifications(UnsubscribePushNotificationsRequest request,
        ServerCallContext context)
    {
        await pushService.UnsubscribeDevice(request.DeviceId);
        return new Empty();
    }

    public override Task<GetWebsocketConnectionStatusResponse> GetWebsocketConnectionStatus(
        GetWebsocketConnectionStatusRequest request, ServerCallContext context)
    {
        var isConnected = request.IdCase switch
        {
            GetWebsocketConnectionStatusRequest.IdOneofCase.DeviceId =>
                websocket.GetDeviceIsConnected(request.DeviceId),
            GetWebsocketConnectionStatusRequest.IdOneofCase.UserId => websocket.GetAccountIsConnected(request.UserId),
            _ => false
        };

        return Task.FromResult(new GetWebsocketConnectionStatusResponse { IsConnected = isConnected });
    }
}
