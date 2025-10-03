using System.Text.Json;
using DysonNetwork.Shared.Proto;
using NATS.Client.Core;

namespace DysonNetwork.Ring.Services;

public class QueueService(INatsConnection nats)
{
    public async Task EnqueueEmail(string toName, string toAddress, string subject, string body)
    {
        var message = new QueueMessage
        {
            Type = QueueMessageType.Email,
            Data = JsonSerializer.Serialize(new EmailMessage
            {
                ToName = toName,
                ToAddress = toAddress,
                Subject = subject,
                Body = body
            })
        };
        var rawMessage = GrpcTypeHelper.ConvertObjectToByteString(message).ToByteArray();
        await nats.PublishAsync(QueueBackgroundService.QueueName, rawMessage);
    }

    public async Task EnqueuePushNotification(Shared.Models.SnNotification notification, Guid userId, bool isSavable = false)
    {
        // Update the account ID in case it wasn't set
        notification.AccountId = userId;

        var message = new QueueMessage
        {
            Type = QueueMessageType.PushNotification,
            TargetId = userId.ToString(),
            Data = JsonSerializer.Serialize(notification)
        };
        var rawMessage = GrpcTypeHelper.ConvertObjectToByteString(message).ToByteArray();
        await nats.PublishAsync(QueueBackgroundService.QueueName, rawMessage);
    }
}

public class QueueMessage
{
    public QueueMessageType Type { get; set; }
    public string? TargetId { get; set; }
    public string Data { get; set; } = string.Empty;
}

public enum QueueMessageType
{
    Email,
    PushNotification
}

public class EmailMessage
{
    public string ToName { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
