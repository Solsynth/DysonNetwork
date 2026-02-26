using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteRingService(DyRingService.DyRingServiceClient ring)
{
    public async Task SendEmail(string toName, string toAddress, string subject, string body)
    {
        var request = new DySendEmailRequest
        {
            Email = new DyEmailMessage
            {
                ToName = toName,
                ToAddress = toAddress,
                Subject = subject,
                Body = body
            }
        };
        await ring.SendEmailAsync(request);
    }

    public async Task PushWebSocketPacket(string accountId, string type, byte[] data, string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketRequest
        {
            UserId = accountId,
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await ring.PushWebSocketPacketAsync(request);
    }

    public async Task PushWebSocketPacketToUsers(List<string> userIds, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToUsersRequest
        {
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };
        request.UserIds.AddRange(userIds);

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await ring.PushWebSocketPacketToUsersAsync(request);
    }

    public async Task PushWebSocketPacketToDevice(string deviceId, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToDeviceRequest
        {
            DeviceId = deviceId,
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await ring.PushWebSocketPacketToDeviceAsync(request);
    }

    public async Task PushWebSocketPacketToDevices(List<string> deviceIds, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToDevicesRequest
        {
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };
        request.DeviceIds.AddRange(deviceIds);

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await ring.PushWebSocketPacketToDevicesAsync(request);
    }

    public async Task SendPushNotificationToUser(
        string userId,
        string topic,
        string title,
        string? subtitle,
        string body,
        byte[]? meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool isSavable = false
    )
    {
        var item = new DyPushNotification
        {
            Topic = topic,
            Title = title,
            Body = body,
            IsSilent = isSilent,
            IsSavable = isSavable
        };
        if (subtitle != null) item.Subtitle = subtitle;
        var request = new DySendPushNotificationToUserRequest
        {
            UserId = userId,
            Notification = item
        };

        if (meta != null)
            request.Notification.Meta = Google.Protobuf.ByteString.CopyFrom(meta);

        if (actionUri != null)
            request.Notification.ActionUri = actionUri;

        await ring.SendPushNotificationToUserAsync(request);
    }

    public async Task SendPushNotificationToUsers(List<string> userIds, string topic, string title, string subtitle,
        string body, byte[]? meta = null, string? actionUri = null, bool isSilent = false, bool isSavable = false)
    {
        var request = new DySendPushNotificationToUsersRequest
        {
            Notification = new DyPushNotification
            {
                Topic = topic,
                Title = title,
                Subtitle = subtitle,
                Body = body,
                IsSilent = isSilent,
                IsSavable = isSavable
            }
        };
        request.UserIds.AddRange(userIds);

        if (meta != null)
        {
            request.Notification.Meta = Google.Protobuf.ByteString.CopyFrom(meta);
        }

        if (actionUri != null)
        {
            request.Notification.ActionUri = actionUri;
        }

        await ring.SendPushNotificationToUsersAsync(request);
    }

    public async Task UnsubscribePushNotifications(string deviceId)
    {
        var request = new DyUnsubscribePushNotificationsRequest
        {
            DeviceId = deviceId
        };
        await ring.UnsubscribePushNotificationsAsync(request);
    }

    public async Task<bool> GetWebsocketConnectionStatus(string deviceIdOrUserId, bool isUserId = false)
    {
        var request = new DyGetWebsocketConnectionStatusRequest();
        if (isUserId)
        {
            request.UserId = deviceIdOrUserId;
        }
        else
        {
            request.DeviceId = deviceIdOrUserId;
        }

        var response = await ring.GetWebsocketConnectionStatusAsync(request);
        return response.IsConnected;
    }

    public async Task<Dictionary<string, bool>> GetWebsocketConnectionStatusBatch(List<string> userIds)
    {
        var request = new DyGetWebsocketConnectionStatusBatchRequest();
        request.UsersId.AddRange(userIds);

        var response = await ring.GetWebsocketConnectionStatusBatchAsync(request);
        return response.IsConnected.ToDictionary();
    }

    public async Task<List<string>> GetAllConnectedUserIds()
    {
        var response = await ring.GetAllConnectedUserIdsAsync(new Google.Protobuf.WellKnownTypes.Empty());
        return response.UserIds.ToList();
    }
}