using DysonNetwork.Shared.Proto;
using Google.Protobuf;

namespace DysonNetwork.Shared.Registry;

public class RemoteRingService(
    DyRingService.DyRingServiceClient ring,
    RemoteWebSocketService? webSocket = null
)
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

    public async Task SendWebSocketPacketToUser(string userId, string type, byte[] data, string? errorMessage = null)
    {
        if (webSocket is null)
            throw new InvalidOperationException("WebSocket delivery is not configured for RemoteRingService.");

        await webSocket.PushWebSocketPacket(userId, type, data, errorMessage);
    }
}
