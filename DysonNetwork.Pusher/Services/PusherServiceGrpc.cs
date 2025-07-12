using DysonNetwork.Pusher.Connection;
using DysonNetwork.Pusher.Email;
using DysonNetwork.Pusher.Notification;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Pusher.Services;

public class PusherServiceGrpc(
    EmailService emailService,
    WebSocketService webSocketService,
    PushService pushService
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
            Data = request.Packet.Data,
            ErrorMessage = request.Packet.ErrorMessage
        };
        webSocketService.SendPacketToAccount(request.UserId, packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToUsers(PushWebSocketPacketToUsersRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = request.Packet.Data,
            ErrorMessage = request.Packet.ErrorMessage
        };
        foreach (var userId in request.UserIds)
            webSocketService.SendPacketToAccount(userId, packet);

        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToDevice(PushWebSocketPacketToDeviceRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = request.Packet.Data,
            ErrorMessage = request.Packet.ErrorMessage
        };
        webSocketService.SendPacketToDevice(request.DeviceId, packet);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> PushWebSocketPacketToDevices(PushWebSocketPacketToDevicesRequest request,
        ServerCallContext context)
    {
        var packet = new Connection.WebSocketPacket
        {
            Type = request.Packet.Type,
            Data = request.Packet.Data,
            ErrorMessage = request.Packet.ErrorMessage
        };
        foreach (var deviceId in request.DeviceIds)
            webSocketService.SendPacketToDevice(deviceId, packet);

        return Task.FromResult(new Empty());
    }

    public override async Task<Empty> SendPushNotification(SendPushNotificationRequest request,
        ServerCallContext context)
    {
        // This is a placeholder implementation. In a real-world scenario, you would
        // need to retrieve the account from the database based on the device token.
        var account = new Account();
        await pushService.SendNotification(
            account,
            request.Notification.Topic,
            request.Notification.Title,
            request.Notification.Subtitle,
            request.Notification.Body,
            GrpcTypeHelper.ConvertFromValueMap(request.Notification.Meta),
            request.Notification.ActionUri,
            request.Notification.IsSilent,
            request.Notification.IsSavable
        );
        return new Empty();
    }

    public override async Task<Empty> SendPushNotificationToDevices(SendPushNotificationToDevicesRequest request,
        ServerCallContext context)
    {
        // This is a placeholder implementation. In a real-world scenario, you would
        // need to retrieve the accounts from the database based on the device tokens.
        var account = new Account();
        foreach (var deviceId in request.DeviceIds)
        {
            await pushService.SendNotification(
                account,
                request.Notification.Topic,
                request.Notification.Title,
                request.Notification.Subtitle,
                request.Notification.Body,
                GrpcTypeHelper.ConvertFromValueMap(request.Notification.Meta),
                request.Notification.ActionUri,
                request.Notification.IsSilent,
                request.Notification.IsSavable
            );
        }

        return new Empty();
    }

    public override async Task<Empty> SendPushNotificationToUser(SendPushNotificationToUserRequest request,
        ServerCallContext context)
    {
        // This is a placeholder implementation. In a real-world scenario, you would
        // need to retrieve the account from the database based on the user ID.
        var account = new Account();
        await pushService.SendNotification(
            account,
            request.Notification.Topic,
            request.Notification.Title,
            request.Notification.Subtitle,
            request.Notification.Body,
            GrpcTypeHelper.ConvertFromValueMap(request.Notification.Meta),
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
            Subtitle = request.Notification.Subtitle, Content = request.Notification.Body,
            Meta = GrpcTypeHelper.ConvertFromValueMap(request.Notification.Meta),
        };
        if (request.Notification.ActionUri is not null)
            notification.Meta["action_uri"] = request.Notification.ActionUri;
        var accounts = request.UserIds.Select(Guid.Parse).ToList();
        await pushService.SendNotificationBatch(notification, accounts, request.Notification.IsSavable);
        return new Empty();
    }
}