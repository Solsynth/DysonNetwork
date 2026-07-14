using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Ring.Services;

public class RingServiceGrpc(QueueService queueService, PushService pushService)
    : DyRingService.DyRingServiceBase
{
    public override async Task<Empty> SendEmail(
        DySendEmailRequest request,
        ServerCallContext context
    )
    {
        await queueService.EnqueueEmail(
            request.Email.ToName,
            request.Email.ToAddress,
            request.Email.Subject,
            request.Email.Body
        );
        return new Empty();
    }

    public override async Task<Empty> SendPushNotificationToUser(
        DySendPushNotificationToUserRequest request,
        ServerCallContext context
    )
    {
        var appId = pushService.ResolveAppId(
            request.Notification.HasAppId ? request.Notification.AppId : null,
            useDefaultIfMissing: true
        );

        await pushService.SendNotification(
            Guid.Parse(request.UserId),
            request.Notification.Topic,
            request.Notification.Title,
            request.Notification.Subtitle,
            request.Notification.Body,
            request.Notification.HasMeta
                ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object?>>(
                    request.Notification.Meta
                ) ?? []
                : [],
            request.Notification.HasActionUri ? request.Notification.ActionUri : null,
            request.Notification.IsSilent,
            request.Notification.IsSavable,
            appId,
            request.Notification.HasPushType ? request.Notification.PushType : null
        );

        return new Empty();
    }

    public override async Task<Empty> SendPushNotificationToUsers(
        DySendPushNotificationToUsersRequest request,
        ServerCallContext context
    )
    {
        var appId = pushService.ResolveAppId(
            request.Notification.HasAppId ? request.Notification.AppId : null,
            useDefaultIfMissing: true
        );

        var userIds = request.UserIds.Select(Guid.Parse).ToList();
        var tasks = userIds.Select(userId =>
            pushService.SendNotification(
                userId,
                request.Notification.Topic,
                request.Notification.Title,
                request.Notification.Subtitle,
                request.Notification.Body,
                request.Notification.HasMeta
                    ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object?>>(
                        request.Notification.Meta
                    ) ?? []
                    : [],
                request.Notification.HasActionUri ? request.Notification.ActionUri : null,
                request.Notification.IsSilent,
                request.Notification.IsSavable,
                appId,
                request.Notification.HasPushType ? request.Notification.PushType : null
            )
        );

        await Task.WhenAll(tasks);
        return new Empty();
    }

    public override async Task<Empty> UnsubscribePushNotifications(
        DyUnsubscribePushNotificationsRequest request,
        ServerCallContext context
    )
    {
        await pushService.UnsubscribeDevice(request.DeviceId);
        return new Empty();
    }
}
