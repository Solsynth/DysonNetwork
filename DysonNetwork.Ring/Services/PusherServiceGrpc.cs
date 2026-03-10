using DysonNetwork.Ring.Notification;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Ring.Services;

public class RingServiceGrpc(
    QueueService queueService,
    PushService pushService
) : DyRingService.DyRingServiceBase
{
    public override async Task<Empty> SendEmail(DySendEmailRequest request, ServerCallContext context)
    {
        await queueService.EnqueueEmail(
            request.Email.ToName,
            request.Email.ToAddress,
            request.Email.Subject,
            request.Email.Body
        );
        return new Empty();
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

    public override async Task<Empty> SendPushNotificationToUsers(DySendPushNotificationToUsersRequest request,
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

    public override async Task<Empty> UnsubscribePushNotifications(DyUnsubscribePushNotificationsRequest request,
        ServerCallContext context)
    {
        await pushService.UnsubscribeDevice(request.DeviceId);
        return new Empty();
    }
}