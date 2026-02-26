using DysonNetwork.Ring.Connection;
using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Ring.Services;

public class RingServiceGrpc(
    QueueService queueService,
    WebSocketService websocket,
    PushService pushService
) : DyRingService.DyRingServiceBase
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
        var packet = Shared.Models.WebSocketPacket.FromProtoValue(request.Packet);

        WebSocketService.SendPacketToAccount(Guid.Parse(request.UserId), packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToUsers(DyPushWebSocketPacketToUsersRequest request,
        ServerCallContext context)
    {
        var packet = Shared.Models.WebSocketPacket.FromProtoValue(request.Packet);

        foreach (var accountId in request.UserIds)
            WebSocketService.SendPacketToAccount(Guid.Parse(accountId), packet);

        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToDevice(PushWebSocketPacketToDeviceRequest request,
        ServerCallContext context)
    {
        var packet = Shared.Models.WebSocketPacket.FromProtoValue(request.Packet);

        websocket.SendPacketToDevice(request.DeviceId, packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToDevices(PushWebSocketPacketToDevicesRequest request,
        ServerCallContext context)
    {
        var packet = Shared.Models.WebSocketPacket.FromProtoValue(request.Packet);

        foreach (var deviceId in request.DeviceIds)
            websocket.SendPacketToDevice(deviceId, packet);

        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> SendPushNotificationToUser(DySendPushNotificationToUserRequest request,
        ServerCallContext context)
    {
        var notification = new SnNotification
        {
            Topic = request.Notification.Topic,
            Title = request.Notification.Title,
            Subtitle = request.Notification.Subtitle,
            Content = request.Notification.Body,
            Meta = request.Notification.HasMeta
                ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object?>>(request.Notification.Meta) ?? []
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
        var notification = new SnNotification
        {
            Topic = request.Notification.Topic,
            Title = request.Notification.Title,
            Subtitle = request.Notification.Subtitle,
            Content = request.Notification.Body,
            Meta = request.Notification.HasMeta
                ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object?>>(request.Notification.Meta) ?? []
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
                WebSocketService.GetDeviceIsConnected(request.DeviceId),
            GetWebsocketConnectionStatusRequest.IdOneofCase.UserId => WebSocketService.GetAccountIsConnected(
                Guid.Parse(request.UserId)),
            _ => false
        };

        return Task.FromResult(new GetWebsocketConnectionStatusResponse { IsConnected = isConnected });
    }

    public override Task<GetWebsocketConnectionStatusBatchResponse> GetWebsocketConnectionStatusBatch(
        GetWebsocketConnectionStatusBatchRequest request, ServerCallContext context)
    {
        var resp = new GetWebsocketConnectionStatusBatchResponse();
        foreach (var id in request.UsersId)
        {
            var gid = Guid.Parse(id);
            resp.IsConnected[id] = WebSocketService.GetAccountIsConnected(gid);
        }

        return Task.FromResult(resp);
    }

    public override Task<GetAllConnectedUserIdsResponse> GetAllConnectedUserIds(Google.Protobuf.WellKnownTypes.Empty request, ServerCallContext context)
    {
        var userIds = WebSocketService.GetAllConnectedUserIds();
        var response = new GetAllConnectedUserIdsResponse();
        response.UserIds.AddRange(userIds.Select(id => id.ToString()));
        return Task.FromResult(response);
    }
}