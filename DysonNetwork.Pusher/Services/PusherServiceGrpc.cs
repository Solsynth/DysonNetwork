using DysonNetwork.Pusher.Connection;
using DysonNetwork.Pusher.Email;
using DysonNetwork.Pusher.Notification;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Pusher.Services;

public class PusherServiceGrpc(
    EmailService emailService,
    WebSocketService websocket,
    PushService pushService,
    AccountClientHelper accountsHelper
) : PusherService.PusherServiceBase
{
    public override async Task<Empty> SendEmail(SendEmailRequest request, ServerCallContext context)
    {
        await emailService.SendEmailAsync(
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
            websocket.SendPacketToAccount(userId, packet);

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
            websocket.SendPacketToDevice(deviceId, packet);

        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> SendPushNotificationToUser(SendPushNotificationToUserRequest request,
        ServerCallContext context)
    {
        var account = await accountsHelper.GetAccount(Guid.Parse(request.UserId));
        await pushService.SendNotification(
            account,
            request.Notification.Topic,
            request.Notification.Title,
            request.Notification.Subtitle,
            request.Notification.Body,
            request.Notification.HasMeta
                ? GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(request.Notification.Meta) ?? []
                : [],
            request.Notification.ActionUri,
            request.Notification.IsSilent,
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
        var accounts = request.UserIds.Select(Guid.Parse).ToList();
        await pushService.SendNotificationBatch(notification, accounts, request.Notification.IsSavable);
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